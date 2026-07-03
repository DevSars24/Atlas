using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Atlas.Infrastructure.Repositories;

public class JobLogRepository : IJobLogRepository
{
    private readonly AtlasDbContext _context;
    private readonly IConnectionMultiplexer? _redis;

    public JobLogRepository(AtlasDbContext context, IConnectionMultiplexer? redis = null)
    {
        _context = context;
        _redis = redis;
    }

    public async Task AddLogAsync(Guid jobId, LogLevel level, string message, string? exception = null, CancellationToken cancellationToken = default)
    {
        var log = new JobLog
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            LogLevel = level,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow,
            Exception = exception
        };
        await _context.JobLogs.AddAsync(log, cancellationToken);

        // Publish real-time log event to Redis Pub/Sub if multiplexer is present
        if (_redis != null)
        {
            try
            {
                var db = _redis.GetDatabase();
                var payload = JsonSerializer.Serialize(new
                {
                    JobId = log.JobId,
                    Level = log.LogLevel.ToString(),
                    Message = log.Message,
                    Timestamp = log.Timestamp
                });
                await db.PublishAsync("atlas:job:logs", payload);
            }
            catch
            {
                // Suppress pub/sub failures to ensure database write is the ultimate source of truth
            }
        }
    }

    public async Task<List<JobLog>> GetLogsForJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.JobLogs
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync(cancellationToken);
    }
}
