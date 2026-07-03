using System;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atlas.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AtlasDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(
        AtlasDbContext context,
        IJobRepository jobs,
        IWorkerRepository workers,
        IJobLogRepository logs,
        IScheduledJobRepository scheduledJobs)
    {
        _context = context;
        Jobs = jobs;
        Workers = workers;
        Logs = logs;
        ScheduledJobs = scheduledJobs;
    }

    public IJobRepository Jobs { get; }
    public IWorkerRepository Workers { get; }
    public IJobLogRepository Logs { get; }
    public IScheduledJobRepository ScheduledJobs { get; }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            return;
        }
        _currentTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            DisposeTransaction();
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
            }
        }
        finally
        {
            DisposeTransaction();
        }
    }

    public void Dispose()
    {
        DisposeTransaction();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void DisposeTransaction()
    {
        if (_currentTransaction != null)
        {
            _currentTransaction.Dispose();
            _currentTransaction = null;
        }
    }
}
