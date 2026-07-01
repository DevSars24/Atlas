using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;

namespace Atlas.Core.Interfaces;

public interface IWorkerRepository
{
    Task<WorkerNode?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<List<WorkerNode>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddOrUpdateHeartbeatAsync(string id, int concurrencyLimit, int activeJobs, CancellationToken cancellationToken = default);
    Task RemoveInactiveWorkersAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}
