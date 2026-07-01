using System;
using System.Threading.Tasks;

namespace Atlas.Infrastructure.Queue;

public interface IRedisJobQueue
{
    Task PushJobIdAsync(string queue, Guid jobId);
    Task<Guid?> DequeueJobIdAsync(string queue, string workerId, TimeSpan timeout);
    Task RemoveFromProcessingAsync(string queue, string workerId, Guid jobId);
    Task RemoveFromQueueAsync(string queue, Guid jobId);
}
