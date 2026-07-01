using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Queue;
using Atlas.Worker.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Worker.Services;

public class JobReaperService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRedisJobQueue _redisQueue;
    private readonly IJobHandlerRegistry _handlerRegistry;
    private readonly ILogger<JobReaperService> _logger;

    public JobReaperService(
        IServiceProvider serviceProvider,
        IRedisJobQueue redisQueue,
        IJobHandlerRegistry handlerRegistry,
        ILogger<JobReaperService> logger)
    {
        _serviceProvider = serviceProvider;
        _redisQueue = redisQueue;
        _handlerRegistry = handlerRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobReaperService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReapStaleWorkersAndJobsAsync(stoppingToken);
                await DispatchDuePendingJobsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during reaping and dispatching cycles");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("JobReaperService stopped");
    }

    private async Task ReapStaleWorkersAndJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var now = DateTimeOffset.UtcNow;
        var staleHeartbeatThreshold = now.AddSeconds(-60);

        // Find stale workers
        var workers = await uow.Workers.GetAllAsync(cancellationToken);
        var staleWorkers = workers.Where(w => w.Status == WorkerStatus.Active && w.LastHeartbeat < staleHeartbeatThreshold).ToList();

        foreach (var worker in staleWorkers)
        {
            _logger.LogWarning("Worker {WorkerId} is stale (last heartbeat: {Heartbeat}). Marking as Inactive.", worker.Id, worker.LastHeartbeat);
            worker.Status = WorkerStatus.Inactive;
            await uow.Workers.RemoveInactiveWorkersAsync(staleHeartbeatThreshold, cancellationToken);
        }

        // Find jobs in Processing that have expired locks or belong to inactive/stale workers
        var activeWorkerIds = workers.Where(w => w.Status == WorkerStatus.Active).Select(w => w.Id).ToHashSet();
        
        // Fetch processing jobs (using high page size to cover current active set)
        var processingJobs = await uow.Jobs.GetJobsAsync(null, JobStatus.Processing, 1, 1000, cancellationToken);
        
        // A job is stale if its lock has expired, or if it is locked by a worker that is no longer active
        var staleJobs = processingJobs.Where(j => 
            (j.LockedUntil.HasValue && j.LockedUntil.Value < now) || 
            (j.LockedBy != null && !activeWorkerIds.Contains(j.LockedBy))
        ).ToList();

        foreach (var job in staleJobs)
        {
            _logger.LogWarning("Reaping stale Job {JobId} (LockedBy: {LockedBy}, LockedUntil: {LockedUntil})", 
                job.Id, job.LockedBy ?? "N/A", job.LockedUntil);

            var retryOptions = _handlerRegistry.GetRetryOptions(job.JobType);

            if (job.Attempts >= retryOptions.MaxAttempts)
            {
                job.Status = JobStatus.DeadLettered;
                job.LockedBy = null;
                job.LockedUntil = null;
                job.LastError = "Reaped: Job lock expired and max attempts exceeded.";
                job.UpdatedAt = now;

                job.StatusHistory.Add(new JobStatusHistory
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    FromStatus = JobStatus.Processing,
                    ToStatus = JobStatus.DeadLettered,
                    Timestamp = now,
                    Notes = $"Job reaped. Attempts ({job.Attempts}) reached limit ({retryOptions.MaxAttempts})."
                });
                await uow.Logs.AddLogAsync(job.Id, Atlas.Core.Domain.LogLevel.Error, "Job lock expired and max attempts exceeded. Moved to DLQ.", cancellationToken: cancellationToken);
            }
            else
            {
                job.Status = JobStatus.Pending;
                job.LockedBy = null;
                job.LockedUntil = null;
                job.UpdatedAt = now;

                job.StatusHistory.Add(new JobStatusHistory
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    FromStatus = JobStatus.Processing,
                    ToStatus = JobStatus.Pending,
                    Timestamp = now,
                    Notes = $"Job reaped. Resetting to Pending (Attempts: {job.Attempts}/{retryOptions.MaxAttempts})."
                });
                await uow.Logs.AddLogAsync(job.Id, Atlas.Core.Domain.LogLevel.Warning, "Job lock expired. Resetting status to Pending for retry.", cancellationToken: cancellationToken);
            }
        }

        if (staleWorkers.Any() || staleJobs.Any())
        {
            await uow.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DispatchDuePendingJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var now = DateTimeOffset.UtcNow;
        
        // Fetch pending jobs (first page)
        var pendingJobs = await uow.Jobs.GetJobsAsync(null, JobStatus.Pending, 1, 500, cancellationToken);
        var dueJobs = pendingJobs.Where(j => j.ScheduledAt <= now).ToList();

        foreach (var job in dueJobs)
        {
            // Re-publish the job ID to Redis. If it's already there, that's fine.
            // Postgres concurrency checks prevent dual execution.
            _logger.LogDebug("DispatchSync: Pushing due Job {JobId} (Queue: '{Queue}') to Redis notification list.", job.Id, job.Queue);
            await _redisQueue.PushJobIdAsync(job.Queue, job.Id);
        }
    }
}
