using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Atlas.Infrastructure.Queue;
using Atlas.Infrastructure.Repositories;
using Atlas.Worker;
using Atlas.Worker.Execution;
using Atlas.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Xunit;

namespace Atlas.IntegrationTests;

public class IntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;

    public IntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private (AtlasDbContext, IUnitOfWork, IRedisJobQueue, ConnectionMultiplexer) CreateSystemContexts()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AtlasDbContext>();
        optionsBuilder.UseNpgsql(_fixture.DbConnectionString);
        var dbContext = new AtlasDbContext(optionsBuilder.Options);

        var jobsRepo = new JobRepository(dbContext);
        var workersRepo = new WorkerRepository(dbContext);
        var logsRepo = new JobLogRepository(dbContext);
        var uow = new UnitOfWork(dbContext, jobsRepo, workersRepo, logsRepo);

        var redisMultiplexer = ConnectionMultiplexer.Connect(_fixture.RedisConnectionString);
        var redisQueue = new RedisJobQueue(redisMultiplexer);

        return (dbContext, uow, redisQueue, redisMultiplexer);
    }

    [Fact]
    public async Task JobEnqueueAndClaimTests_ShouldEnqueueAndClaimCorrectly()
    {
        // Arrange
        var (db, uow, redisQueue, redis) = CreateSystemContexts();
        var jobId = Guid.NewGuid();
        var job = new Job
        {
            Id = jobId,
            Queue = "test-queue",
            JobType = "TestJob",
            Payload = "{\"Message\":\"SuccessTest\"}",
            Priority = JobPriority.High,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow
        };

        // Act - Enqueue in Postgres
        await uow.Jobs.AddAsync(job);
        await uow.SaveChangesAsync();

        // Act - Push to Redis
        await redisQueue.PushJobIdAsync("test-queue", jobId);

        // Assert - Dequeue from Redis
        var poppedId = await redisQueue.DequeueJobIdAsync("test-queue", "worker-1", TimeSpan.FromSeconds(1));
        Assert.NotNull(poppedId);
        Assert.Equal(jobId, poppedId.Value);

        // Act - Claim in Postgres
        var claimedJob = await uow.Jobs.ClaimNextJobAsync("test-queue", "worker-1", TimeSpan.FromMinutes(5));
        
        // Assert - Claimed job properties
        Assert.NotNull(claimedJob);
        Assert.Equal(jobId, claimedJob.Id);
        Assert.Equal(JobStatus.Processing, claimedJob.Status);
        Assert.Equal("worker-1", claimedJob.LockedBy);
        Assert.NotNull(claimedJob.LockedUntil);
        Assert.Equal(1, claimedJob.Attempts);

        // Cleanup
        await redisQueue.RemoveFromProcessingAsync("test-queue", "worker-1", jobId);
        db.Jobs.Remove(claimedJob);
        await db.SaveChangesAsync();
        db.Dispose();
        redis.Dispose();
    }

    [Fact]
    public async Task RetryAndDeadLetterTests_ShouldRetryAndDLQAfterMaxAttempts()
    {
        // Arrange
        var (db, uow, redisQueue, redis) = CreateSystemContexts();
        var jobId = Guid.NewGuid();
        var job = new Job
        {
            Id = jobId,
            Queue = "default",
            JobType = "FailingJob",
            Payload = "{\"Message\":\"FailTest\"}",
            Priority = JobPriority.Normal,
            Status = JobStatus.Processing, // Assume it was just claimed
            Attempts = 1, // First attempt
            MaxAttempts = 2, // Max attempts is 2
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow,
            LockedBy = "worker-2",
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5)
        };

        await uow.Jobs.AddAsync(job);
        await uow.SaveChangesAsync();

        // Setup Registry with a failing handler
        var registry = new JobHandlerRegistry();
        registry.RegisterHandler<FailingTestHandler, TestJobPayload>("FailingJob");
        registry.RegisterRetryOptions("FailingJob", new RetryOptions { MaxAttempts = 2, InitialDelay = TimeSpan.FromMilliseconds(10) });

        var services = new ServiceCollection();
        services.AddTransient<FailingTestHandler>();
        var serviceProvider = services.BuildServiceProvider();

        // Instantiate Executor Service
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var executorLogger = loggerFactory.CreateLogger<JobExecutorService>();
        var workerInfo = new WorkerInfo("worker-2", 5);

        var executor = new JobExecutorService(uow, redisQueue, registry, workerInfo, serviceProvider, executorLogger);

        // Act - Run 1st execution (attempt 1 -> fails -> goes back to Pending because max attempts is 2)
        await executor.ExecuteAsync(jobId, "default", CancellationToken.None);

        // Assert - After 1st failure
        var jobAfterFirstFail = await uow.Jobs.GetByIdAsync(jobId);
        Assert.NotNull(jobAfterFirstFail);
        Assert.Equal(JobStatus.Pending, jobAfterFirstFail.Status);
        Assert.Null(jobAfterFirstFail.LockedBy);
        Assert.Equal(1, jobAfterFirstFail.Attempts);

        // Act - Simulate 2nd Claim & Execution (Attempts goes to 2)
        var claimedJob = await uow.Jobs.ClaimNextJobAsync("default", "worker-2", TimeSpan.FromMinutes(5));
        Assert.NotNull(claimedJob);
        Assert.Equal(2, claimedJob.Attempts);

        // Run 2nd execution (attempt 2 -> fails -> dead lettered because attempts >= max attempts)
        await executor.ExecuteAsync(jobId, "default", CancellationToken.None);

        // Assert - Final DLQ status
        var jobAfterSecondFail = await uow.Jobs.GetByIdAsync(jobId);
        Assert.NotNull(jobAfterSecondFail);
        Assert.Equal(JobStatus.DeadLettered, jobAfterSecondFail.Status);
        Assert.Null(jobAfterSecondFail.LockedBy);
        Assert.Equal(2, jobAfterSecondFail.Attempts);
        Assert.Contains("Simulated integration failure", jobAfterSecondFail.LastError);

        // Cleanup
        db.Jobs.Remove(jobAfterSecondFail);
        await db.SaveChangesAsync();
        db.Dispose();
        redis.Dispose();
    }

    [Fact]
    public async Task HeartbeatReaperTests_ShouldReclaimJobsFromStaleWorkers()
    {
        // Arrange
        var (db, uow, redisQueue, redis) = CreateSystemContexts();
        
        // Add a stale worker
        var staleWorker = new WorkerNode
        {
            Id = "crashed-worker",
            Status = WorkerStatus.Active,
            LastHeartbeat = DateTimeOffset.UtcNow.AddMinutes(-5), // 5 mins ago (stale > 60s)
            ConcurrencyLimit = 5,
            ActiveJobs = 1
        };
        await db.Workers.AddAsync(staleWorker);

        // Add a job locked by the stale worker
        var jobId = Guid.NewGuid();
        var job = new Job
        {
            Id = jobId,
            Queue = "default",
            JobType = "TestJob",
            Payload = "{}",
            Priority = JobPriority.Normal,
            Status = JobStatus.Processing,
            Attempts = 1,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow,
            LockedBy = "crashed-worker",
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        await db.Jobs.AddAsync(job);
        await db.SaveChangesAsync();

        // Instantiate Reaper Service
        var registry = new JobHandlerRegistry();
        registry.RegisterHandler<SuccessTestHandler, TestJobPayload>("TestJob");

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var reaperLogger = loggerFactory.CreateLogger<JobReaperService>();
        
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var reaper = new JobReaperService(serviceProvider, redisQueue, registry, reaperLogger);

        // Act - Trigger Reaper logic manually using private method invocation to avoid starting background loop
        var method = typeof(JobReaperService).GetMethod("ReapStaleWorkersAndJobsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var task = (Task)method.Invoke(reaper, new object[] { CancellationToken.None })!;
        await task;

        // Assert - Worker is marked inactive
        var workerState = await uow.Workers.GetByIdAsync("crashed-worker");
        Assert.NotNull(workerState);
        Assert.Equal(WorkerStatus.Inactive, workerState.Status);

        // Assert - Job is reset to Pending
        var jobState = await uow.Jobs.GetByIdAsync(jobId);
        Assert.NotNull(jobState);
        Assert.Equal(JobStatus.Pending, jobState.Status);
        Assert.Null(jobState.LockedBy);
        Assert.Null(jobState.LockedUntil);

        // Cleanup
        db.Workers.Remove(workerState);
        db.Jobs.Remove(jobState);
        await db.SaveChangesAsync();
        db.Dispose();
        redis.Dispose();
    }

    [Fact]
    public async Task IdempotencyTests_ShouldPreventDuplicateSubmissions()
    {
        // Arrange
        var (db, uow, redisQueue, redis) = CreateSystemContexts();
        var uniqueKey = "idem-key-" + Guid.NewGuid();

        var job1 = new Job
        {
            Id = Guid.NewGuid(),
            Queue = "default",
            JobType = "TestJob",
            Payload = "{}",
            Priority = JobPriority.Normal,
            Status = JobStatus.Pending,
            Attempts = 0,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow,
            IdempotencyKey = uniqueKey
        };

        var job2 = new Job
        {
            Id = Guid.NewGuid(),
            Queue = "default",
            JobType = "TestJob",
            Payload = "{}",
            Priority = JobPriority.Normal,
            Status = JobStatus.Pending,
            Attempts = 0,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow,
            IdempotencyKey = uniqueKey
        };

        // Act - Add first job
        await uow.Jobs.AddAsync(job1);
        await uow.SaveChangesAsync();

        // Act & Assert - Adding second job with same key throws db exception
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await uow.Jobs.AddAsync(job2);
            await uow.SaveChangesAsync();
        });

        // Cleanup
        db.Jobs.Remove(job1);
        await db.SaveChangesAsync();
        db.Dispose();
        redis.Dispose();
    }
}

// Stub Handlers for Integration Testing
public class FailingTestHandler : IJobHandler<TestJobPayload>
{
    public Task ExecuteAsync(Guid jobId, TestJobPayload payload, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated integration failure");
    }
}

public class SuccessTestHandler : IJobHandler<TestJobPayload>
{
    public Task ExecuteAsync(Guid jobId, TestJobPayload payload, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
