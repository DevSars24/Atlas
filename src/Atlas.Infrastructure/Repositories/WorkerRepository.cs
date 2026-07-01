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

public class WorkerRepository : IWorkerRepository
{
    private readonly AtlasDbContext _context;

    public WorkerRepository(AtlasDbContext context)
    {
        _context = context;
    }

    public async Task<WorkerNode?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Workers.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<List<WorkerNode>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Workers.ToListAsync(cancellationToken);
    }

    public async Task AddOrUpdateHeartbeatAsync(string id, int concurrencyLimit, int activeJobs, CancellationToken cancellationToken = default)
    {
        var worker = await _context.Workers.FindAsync(new object[] { id }, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (worker == null)
        {
            worker = new WorkerNode
            {
                Id = id,
                Status = WorkerStatus.Active,
                LastHeartbeat = now,
                ConcurrencyLimit = concurrencyLimit,
                ActiveJobs = activeJobs
            };
            await _context.Workers.AddAsync(worker, cancellationToken);
        }
        else
        {
            worker.Status = WorkerStatus.Active;
            worker.LastHeartbeat = now;
            worker.ConcurrencyLimit = concurrencyLimit;
            worker.ActiveJobs = activeJobs;
            _context.Workers.Update(worker);
        }
    }

    public async Task RemoveInactiveWorkersAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        var inactiveWorkers = await _context.Workers
            .Where(w => w.LastHeartbeat < threshold)
            .ToListAsync(cancellationToken);

        if (inactiveWorkers.Any())
        {
            _context.Workers.RemoveRange(inactiveWorkers);
        }
    }
}
