using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atlas.Infrastructure.Repositories;

public class JobRepository : IJobRepository
{
    private readonly AtlasDbContext _context;

    public JobRepository(AtlasDbContext context)
    {
        _context = context;
    }

    public async Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<Job?> GetByIdWithRelationsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Include(j => j.StatusHistory)
            .Include(j => j.Logs)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<Job?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .FirstOrDefaultAsync(j => j.IdempotencyKey == key, cancellationToken);
    }

    public async Task<List<Job>> GetJobsAsync(string? queue, JobStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Jobs.AsQueryable();

        if (!string.IsNullOrEmpty(queue))
        {
            query = query.Where(j => j.Queue == queue);
        }

        if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
        }

        return await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<Job?> ClaimNextJobAsync(string queue, string workerId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var lockedUntil = now.Add(lockDuration);

        var queueParam = new NpgsqlParameter("Queue", queue);
        var workerIdParam = new NpgsqlParameter("WorkerId", workerId);
        var nowParam = new NpgsqlParameter("Now", now);
        var lockedUntilParam = new NpgsqlParameter("LockedUntil", lockedUntil);

        // Raw SQL with Common Table Expression (CTE) using FOR UPDATE SKIP LOCKED
        var sql = @"
            WITH next_job AS (
                SELECT ""Id""
                FROM ""Jobs""
                WHERE ""Status"" = 0 -- Pending
                  AND ""Queue"" = @Queue
                  AND ""ScheduledAt"" <= @Now
                  AND (""LockedUntil"" IS NULL OR ""LockedUntil"" < @Now)
                ORDER BY ""Priority"" DESC, ""ScheduledAt"" ASC, ""CreatedAt"" ASC
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            UPDATE ""Jobs""
            SET ""Status"" = 1, -- Processing
                ""LockedUntil"" = @LockedUntil,
                ""LockedBy"" = @WorkerId,
                ""Attempts"" = ""Attempts"" + 1,
                ""UpdatedAt"" = @Now
            FROM next_job
            WHERE ""Jobs"".""Id"" = next_job.""Id""
            RETURNING ""Jobs"".""Id"", ""Jobs"".""Queue"", ""Jobs"".""JobType"", ""Jobs"".""Status"", ""Jobs"".""Payload"", ""Jobs"".""Priority"", ""Jobs"".""Attempts"", ""Jobs"".""MaxAttempts"", ""Jobs"".""ScheduledAt"", ""Jobs"".""CreatedAt"", ""Jobs"".""UpdatedAt"", ""Jobs"".""LockedUntil"", ""Jobs"".""LockedBy"", ""Jobs"".""IdempotencyKey"", ""Jobs"".""LastError"";";

        // Execute the update query and return the single claimed job entity (if any)
        var jobs = await _context.Jobs
            .FromSqlRaw(sql, queueParam, nowParam, lockedUntilParam, workerIdParam)
            .ToListAsync(cancellationToken);

        return jobs.FirstOrDefault();
    }

    public async Task AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        await _context.Jobs.AddAsync(job, cancellationToken);
    }

    public Task UpdateAsync(Job job, CancellationToken cancellationToken = default)
    {
        _context.Jobs.Update(job);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Job job, CancellationToken cancellationToken = default)
    {
        _context.Jobs.Remove(job);
        return Task.CompletedTask;
    }

    public async Task<List<Job>> GetStaleJobsAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Where(j => j.Status == JobStatus.Processing && j.LockedUntil < threshold)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(JobStatus? status = null, CancellationToken cancellationToken = default)
    {
        if (status.HasValue)
        {
            return await _context.Jobs.CountAsync(j => j.Status == status.Value, cancellationToken);
        }
        return await _context.Jobs.CountAsync(cancellationToken);
    }
}
