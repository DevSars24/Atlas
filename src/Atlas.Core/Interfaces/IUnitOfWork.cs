using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atlas.Core.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IJobRepository Jobs { get; }
    IWorkerRepository Workers { get; }
    IJobLogRepository Logs { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
