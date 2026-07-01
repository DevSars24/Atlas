using System;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Infrastructure.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Worker.Services;

public class JobPollingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRedisJobQueue _redisQueue;
    private readonly WorkerInfo _workerInfo;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly ILogger<JobPollingService> _logger;
    private readonly string[] _queues;

    public JobPollingService(
        IServiceProvider serviceProvider,
        IRedisJobQueue redisQueue,
        WorkerInfo workerInfo,
        ILogger<JobPollingService> logger)
    {
        _serviceProvider = serviceProvider;
        _redisQueue = redisQueue;
        _workerInfo = workerInfo;
        _concurrencySemaphore = new SemaphoreSlim(workerInfo.ConcurrencyLimit);
        _logger = logger;
        
        // Polling default queue. Can be extended to read from configuration.
        _queues = new[] { "default" };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobPollingService started. Concurrency: {Limit}, Queues: {Queues}", 
            _workerInfo.ConcurrencyLimit, string.Join(", ", _queues));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _concurrencySemaphore.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            bool jobDispatched = false;

            try
            {
                foreach (var queue in _queues)
                {
                    // Dequeue from Redis using BRPOPLPUSH wrapper (blocks up to 5s if empty)
                    var jobId = await _redisQueue.DequeueJobIdAsync(queue, _workerInfo.Id, TimeSpan.FromSeconds(5));
                    
                    if (jobId.HasValue)
                    {
                        _logger.LogDebug("Fetched Job {JobId} from Redis queue {Queue}", jobId.Value, queue);
                        
                        // Fire and forget task to execute the job, passing semaphore release responsibility
                        _ = ExecuteJobAndReleaseSemaphoreAsync(jobId.Value, queue, stoppingToken);
                        jobDispatched = true;
                        break; // Break loop to re-wait on semaphore
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while polling for jobs");
            }

            if (!jobDispatched)
            {
                _concurrencySemaphore.Release();
                
                // Small delay to prevent spamming if Redis is empty or throws error
                try
                {
                    await Task.Delay(1000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("JobPollingService stopping...");
    }

    private async Task ExecuteJobAndReleaseSemaphoreAsync(Guid jobId, string queue, CancellationToken stoppingToken)
    {
        _workerInfo.IncrementActiveJobs();
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<JobExecutorService>();
            await executor.ExecuteAsync(jobId, queue, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical: Unhandled exception in job executor loop for Job {JobId}", jobId);
        }
        finally
        {
            _workerInfo.DecrementActiveJobs();
            _concurrencySemaphore.Release();
        }
    }
}
