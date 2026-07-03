using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;

namespace Atlas.Core.Interfaces;

public interface IScheduledJobRepository
{
    Task<List<ScheduledJob>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<ScheduledJob>> GetDueJobsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<ScheduledJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ScheduledJob job, CancellationToken cancellationToken = default);
    Task UpdateAsync(ScheduledJob job, CancellationToken cancellationToken = default);
    Task DeleteAsync(ScheduledJob job, CancellationToken cancellationToken = default);
}
