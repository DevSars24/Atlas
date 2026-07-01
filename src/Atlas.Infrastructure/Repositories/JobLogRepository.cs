using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Repositories;

public class JobLogRepository : IJobLogRepository
{
    private readonly AtlasDbContext _context;

    public JobLogRepository(AtlasDbContext context)
    {
        _context = context;
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
    }

    public async Task<List<JobLog>> GetLogsForJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.JobLogs
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync(cancellationToken);
    }
}
