using System;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Atlas.Infrastructure.Queue;
using Atlas.Infrastructure.Repositories;
using Atlas.Worker.Execution;
using Atlas.Worker.Jobs;
using Atlas.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Atlas.Worker;

// ── Legacy TestJob (kept for backwards compat) ────────────────────────────────

public class TestJobPayload
{
    public string Message { get; set; } = string.Empty;
    public bool Fail { get; set; }
}

public class TestJobHandler : IJobHandler<TestJobPayload>
{
    private readonly ILogger<TestJobHandler> _logger;

    public TestJobHandler(ILogger<TestJobHandler> logger) => _logger = logger;

    public async Task ExecuteAsync(Guid jobId, TestJobPayload payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TestJobHandler processing Job {JobId}. Message: '{Message}'", jobId, payload.Message);
        await Task.Delay(1000, cancellationToken);

        if (payload.Fail)
        {
            _logger.LogWarning("TestJobHandler: Simulating failure for Job {JobId}", jobId);
            throw new InvalidOperationException($"Simulated failure: {payload.Message}");
        }

        _logger.LogInformation("TestJobHandler: Job {JobId} successfully processed", jobId);
    }
}

// ── Program ───────────────────────────────────────────────────────────────────

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                var configuration = hostContext.Configuration;

                var dbConnString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                                   ?? configuration.GetConnectionString("DefaultConnection")
                                   ?? "Host=localhost;Database=atlas;Username=postgres;Password=postgres";

                var redisConnString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
                                      ?? configuration.GetConnectionString("RedisConnection")
                                      ?? "localhost:6379";

                var workerId         = Environment.GetEnvironmentVariable("WORKER_ID");
                var concurrencyLimit = int.TryParse(Environment.GetEnvironmentVariable("CONCURRENCY_LIMIT"), out var limit) ? limit : 5;
                if (concurrencyLimit <= 0) concurrencyLimit = 5;

                // Worker identity
                services.AddSingleton(new WorkerInfo(workerId, concurrencyLimit));

                // Database
                services.AddDbContext<AtlasDbContext>(o => o.UseNpgsql(dbConnString));

                // Redis
                services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnString));
                services.AddSingleton<IRedisJobQueue, RedisJobQueue>();

                // Repositories
                services.AddScoped<IJobRepository, JobRepository>();
                services.AddScoped<IWorkerRepository, WorkerRepository>();
                services.AddScoped<IJobLogRepository, JobLogRepository>();
                services.AddScoped<IScheduledJobRepository, ScheduledJobRepository>();
                services.AddScoped<IUnitOfWork, UnitOfWork>();

                // Handler registry
                var registry = new JobHandlerRegistry();

                // ── Register all job handlers ──────────────────────────────────
                registry.RegisterHandler<TestJobHandler, TestJobPayload>("TestJob");
                registry.RegisterRetryOptions("TestJob", new RetryOptions
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromSeconds(3),
                    BackoffMultiplier = 2.0,
                    MaxDelay = TimeSpan.FromSeconds(30)
                });

                registry.RegisterHandler<SendEmailJobHandler, SendEmailPayload>("SendEmailJob");
                registry.RegisterRetryOptions("SendEmailJob", new RetryOptions
                {
                    MaxAttempts = 5,
                    InitialDelay = TimeSpan.FromSeconds(5),
                    BackoffMultiplier = 2.0,
                    MaxDelay = TimeSpan.FromMinutes(2)
                });

                registry.RegisterHandler<GenerateReportJobHandler, GenerateReportPayload>("GenerateReportJob");
                registry.RegisterRetryOptions("GenerateReportJob", new RetryOptions
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromSeconds(10),
                    BackoffMultiplier = 1.5,
                    MaxDelay = TimeSpan.FromMinutes(5)
                });

                services.AddSingleton<IJobHandlerRegistry>(registry);

                // Job executor (scoped — one per job execution)
                services.AddScoped<JobExecutorService>();

                // Concrete handler types (DI resolves them via IServiceProvider)
                services.AddTransient<TestJobHandler>();
                services.AddTransient<SendEmailJobHandler>();
                services.AddTransient<GenerateReportJobHandler>();

                // Background services
                services.AddHostedService<WorkerRegistrationService>();
                services.AddHostedService<HeartbeatService>();
                services.AddHostedService<JobPollingService>();
                services.AddHostedService<JobReaperService>();
            })
            .Build();

        await host.RunAsync();
    }
}
