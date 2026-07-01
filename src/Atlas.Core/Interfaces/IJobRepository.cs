using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;

namespace Atlas.Core.Interfaces;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Job?> GetByIdWithRelationsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Job?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<List<Job>> GetJobsAsync(string? queue, JobStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Job?> ClaimNextJobAsync(string queue, string workerId, TimeSpan lockDuration, CancellationToken cancellationToken = default);
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    Task UpdateAsync(Job job, CancellationToken cancellationToken = default);
    Task DeleteAsync(Job job, CancellationToken cancellationToken = default);
    Task<List<Job>> GetStaleJobsAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(JobStatus? status = null, CancellationToken cancellationToken = default);
}
