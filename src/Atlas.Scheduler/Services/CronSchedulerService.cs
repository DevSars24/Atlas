using System;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Atlas.Infrastructure.Queue;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Scheduler.Services;

/// <summary>
/// Cron scheduler with Postgres advisory-lock-based leader election.
/// Only one instance (across N replicas) holds the lock and fires schedules.
/// </summary>
public class CronSchedulerService : BackgroundService
{
    // Arbitrary stable lock id — all scheduler instances compete for the same key
    private const long AdvisoryLockId = 9876543210L;
    private const int TickIntervalSeconds = 30;

    private readonly IServiceProvider _services;
    private readonly ILogger<CronSchedulerService> _logger;

    public CronSchedulerService(IServiceProvider services, ILogger<CronSchedulerService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CronSchedulerService starting. Tick interval: {Interval}s.", TickIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error during scheduler tick.");
            }

            await Task.Delay(TimeSpan.FromSeconds(TickIntervalSeconds), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var redisQueue = scope.ServiceProvider.GetRequiredService<IRedisJobQueue>();

        // Attempt Postgres session-level advisory lock (non-blocking, returns bool)
        var isLeader = await TryAcquireLeaderLockAsync(dbContext, cancellationToken);
        if (!isLeader)
        {
            _logger.LogDebug("Not the leader — skipping scheduler tick.");
            return;
        }

        _logger.LogInformation("Acquired scheduler leader lock. Evaluating due schedules...");

        var now = DateTimeOffset.UtcNow;

        // Initialise NextRunAt for schedules that don't have it
        var allSchedules = await uow.ScheduledJobs.GetAllAsync(cancellationToken);
        var needsSave = false;
        foreach (var s in allSchedules)
        {
            if (!s.NextRunAt.HasValue && s.IsEnabled)
            {
                s.NextRunAt = ComputeNextRunAt(s.CronExpression, now);
                await uow.ScheduledJobs.UpdateAsync(s, cancellationToken);
                needsSave = true;
            }
        }
        if (needsSave) await uow.SaveChangesAsync(cancellationToken);

        // Process due schedules
        var dueSchedules = await uow.ScheduledJobs.GetDueJobsAsync(now, cancellationToken);
        foreach (var schedule in dueSchedules)
        {
            try
            {
                await EnqueueScheduledJobAsync(schedule, now, uow, redisQueue, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue scheduled job {Id} ({Name})", schedule.Id, schedule.Name);
            }
        }
    }

    private async Task EnqueueScheduledJobAsync(
        ScheduledJob schedule,
        DateTimeOffset now,
        IUnitOfWork uow,
        IRedisJobQueue redisQueue,
        CancellationToken cancellationToken)
    {
        // Misfire: if we're more than 5 min late and policy is Skip, advance without running
        if (schedule.MisfirePolicy == MisfirePolicy.Skip &&
            schedule.NextRunAt.HasValue &&
            now - schedule.NextRunAt.Value > TimeSpan.FromMinutes(5))
        {
            _logger.LogWarning("Schedule '{Name}' misfired (policy=Skip). Advancing NextRunAt.", schedule.Name);
            schedule.LastRunAt = now;
            schedule.NextRunAt = ComputeNextRunAt(schedule.CronExpression, now);
            await uow.ScheduledJobs.UpdateAsync(schedule, cancellationToken);
            await uow.SaveChangesAsync(cancellationToken);
            return;
        }

        var job = new Job
        {
            Id = Guid.NewGuid(),
            Queue = schedule.Queue,
            JobType = schedule.JobType,
            Payload = schedule.Payload,
            Priority = schedule.Priority,
            Status = JobStatus.Pending,
            Attempts = 0,
            MaxAttempts = schedule.MaxAttempts,
            ScheduledAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        job.StatusHistory.Add(new JobStatusHistory
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            FromStatus = JobStatus.Pending,
            ToStatus = JobStatus.Pending,
            Timestamp = now,
            Notes = $"Enqueued by scheduler '{schedule.Name}' (cron: {schedule.CronExpression})"
        });

        await uow.Jobs.AddAsync(job, cancellationToken);

        schedule.LastRunAt = now;
        schedule.NextRunAt = ComputeNextRunAt(schedule.CronExpression, now);
        await uow.ScheduledJobs.UpdateAsync(schedule, cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);
        await redisQueue.PushJobIdAsync(job.Queue, job.Id);

        _logger.LogInformation("Enqueued job {JobId} for schedule '{Name}'. Next: {Next}",
            job.Id, schedule.Name, schedule.NextRunAt);
    }

    private static DateTimeOffset? ComputeNextRunAt(string cronExpression, DateTimeOffset from)
    {
        try
        {
            var expr = CronExpression.Parse(cronExpression, CronFormat.Standard);
            var next = expr.GetNextOccurrence(from.UtcDateTime, TimeZoneInfo.Utc);
            return next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TryAcquireLeaderLockAsync(AtlasDbContext db, CancellationToken ct)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT pg_try_advisory_lock({AdvisoryLockId})";
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
