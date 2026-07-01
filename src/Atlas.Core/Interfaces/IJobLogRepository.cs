using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;

namespace Atlas.Core.Interfaces;

public interface IJobLogRepository
{
    Task AddLogAsync(Guid jobId, LogLevel level, string message, string? exception = null, CancellationToken cancellationToken = default);
    Task<List<JobLog>> GetLogsForJobAsync(Guid jobId, CancellationToken cancellationToken = default);
}
