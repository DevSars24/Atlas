using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Atlas.Infrastructure.Queue;

public class RedisJobQueue : IRedisJobQueue
{
    private readonly IDatabase _db;

    public RedisJobQueue(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }

    public async Task PushJobIdAsync(string queue, Guid jobId)
    {
        await _db.ListLeftPushAsync($"queue:{queue}", jobId.ToString());
    }

    public async Task<Guid?> DequeueJobIdAsync(string queue, string workerId, TimeSpan timeout)
    {
        var sourceKey = $"queue:{queue}";
        var destKey = $"queue:{queue}:{workerId}:processing";
        
        // Execute BRPOPLPUSH as raw command to support blocking with timeout
        var timeoutSeconds = (int)Math.Max(1, timeout.TotalSeconds);
        var result = await _db.ExecuteAsync("BRPOPLPUSH", sourceKey, destKey, timeoutSeconds);
        
        if (result.IsNull)
        {
            return null;
        }

        var value = (string?)result;
        if (string.IsNullOrEmpty(value) || !Guid.TryParse(value, out var jobId))
        {
            return null;
        }

        return jobId;
    }

    public async Task RemoveFromProcessingAsync(string queue, string workerId, Guid jobId)
    {
        await _db.ListRemoveAsync($"queue:{queue}:{workerId}:processing", jobId.ToString());
    }

    public async Task RemoveFromQueueAsync(string queue, Guid jobId)
    {
        await _db.ListRemoveAsync($"queue:{queue}", jobId.ToString());
    }
}
