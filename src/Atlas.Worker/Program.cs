using System;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Atlas.Infrastructure.Queue;
using Atlas.Infrastructure.Repositories;
using Atlas.Worker.Execution;
using Atlas.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Atlas.Worker;

// Demo payload and handler classes
public class TestJobPayload
{
    public string Message { get; set; } = string.Empty;
    public bool Fail { get; set; }
}

public class TestJobHandler : IJobHandler<TestJobPayload>
{
    private readonly ILogger<TestJobHandler> _logger;

    public TestJobHandler(ILogger<TestJobHandler> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid jobId, TestJobPayload payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TestJobHandler processing Job {JobId}. Message: '{Message}'", jobId, payload.Message);
        
        // Simulate processing work
        await Task.Delay(1000, cancellationToken);

        if (payload.Fail)
        {
            _logger.LogWarning("TestJobHandler: Simulating failure for Job {JobId}", jobId);
            throw new InvalidOperationException($"Simulated failure: {payload.Message}");
        }

        _logger.LogInformation("TestJobHandler: Job {JobId} successfully processed", jobId);
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                var configuration = hostContext.Configuration;

                // Load connection strings from env variables or appsettings
                var dbConnString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") 
                                   ?? configuration.GetConnectionString("DefaultConnection") 
                                   ?? "Host=localhost;Database=atlas;Username=postgres;Password=postgres";

                var redisConnString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") 
                                      ?? configuration.GetConnectionString("RedisConnection") 
                                      ?? "localhost:6379";

                var workerId = Environment.GetEnvironmentVariable("WORKER_ID");
                var concurrencyLimitStr = Environment.GetEnvironmentVariable("CONCURRENCY_LIMIT");
                int.TryParse(concurrencyLimitStr, out var limit);
                if (limit <= 0) limit = 5;

                // Register Worker Info
                var workerInfo = new WorkerInfo(workerId, limit);
                services.AddSingleton(workerInfo);

                // Register DbContext
                services.AddDbContext<AtlasDbContext>(options =>
                    options.UseNpgsql(dbConnString));

                // Register Redis Connection Multiplexer
                services.AddSingleton<IConnectionMultiplexer>(_ => 
                    ConnectionMultiplexer.Connect(redisConnString));

                // Register Redis Job Queue
                services.AddSingleton<IRedisJobQueue, RedisJobQueue>();

                // Register Repositories and Unit of Work
                services.AddScoped<IJobRepository, JobRepository>();
                services.AddScoped<IWorkerRepository, WorkerRepository>();
                services.AddScoped<IJobLogRepository, JobLogRepository>();
                services.AddScoped<IUnitOfWork, UnitOfWork>();

                // Register Handler Registry
                var registry = new JobHandlerRegistry();
                // Register our demo job type
                registry.RegisterHandler<TestJobHandler, TestJobPayload>("TestJob");
                
                // Per-job-type RetryOptions demo: TestJob retries up to 3 times, starts at 3 seconds initial delay
                registry.RegisterRetryOptions("TestJob", new RetryOptions
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromSeconds(3),
                    BackoffMultiplier = 2.0,
                    MaxDelay = TimeSpan.FromSeconds(30)
                });
                
                services.AddSingleton<IJobHandlerRegistry>(registry);

                // Register job executor (scoped to run within worker scope per job)
                services.AddScoped<JobExecutorService>();

                // Register Handlers
                services.AddTransient<TestJobHandler>();

                // Register Background Services
                services.AddHostedService<WorkerRegistrationService>();
                services.AddHostedService<HeartbeatService>();
                services.AddHostedService<JobPollingService>();
                services.AddHostedService<JobReaperService>();
            })
            .Build();

        await host.RunAsync();
    }
}
