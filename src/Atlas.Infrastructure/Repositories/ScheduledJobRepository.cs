using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Repositories;

public class ScheduledJobRepository : IScheduledJobRepository
{
    private readonly AtlasDbContext _context;

    public ScheduledJobRepository(AtlasDbContext context)
    {
        _context = context;
    }

    public async Task<List<ScheduledJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ScheduledJobs
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ScheduledJob>> GetDueJobsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return await _context.ScheduledJobs
            .Where(s => s.IsEnabled && s.NextRunAt.HasValue && s.NextRunAt.Value <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task<ScheduledJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ScheduledJobs.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task AddAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        await _context.ScheduledJobs.AddAsync(job, cancellationToken);
    }

    public Task UpdateAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        _context.ScheduledJobs.Update(job);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        _context.ScheduledJobs.Remove(job);
        return Task.CompletedTask;
    }
}
