using System;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Queue;
using Atlas.Worker.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.Worker.Services;

public class JobExecutorService
{
    private readonly IUnitOfWork _uow;
    private readonly IRedisJobQueue _redisQueue;
    private readonly IJobHandlerRegistry _handlerRegistry;
    private readonly WorkerInfo _workerInfo;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobExecutorService> _logger;

    public JobExecutorService(
        IUnitOfWork uow,
        IRedisJobQueue redisQueue,
        IJobHandlerRegistry handlerRegistry,
        WorkerInfo workerInfo,
        IServiceProvider serviceProvider,
        ILogger<JobExecutorService> logger)
    {
        _uow = uow;
        _redisQueue = redisQueue;
        _handlerRegistry = handlerRegistry;
        _workerInfo = workerInfo;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid jobId, string queue, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker {WorkerId} starting execution of Job {JobId}", _workerInfo.Id, jobId);

        // Fetch job from Postgres (source of truth)
        var job = await _uow.Jobs.GetByIdAsync(jobId, cancellationToken);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found in database. Removing from processing queue.", jobId);
            await _redisQueue.RemoveFromProcessingAsync(queue, _workerInfo.Id, jobId);
            return;
        }

        // Verify the job is locked by this worker and is in Processing status
        if (job.LockedBy != _workerInfo.Id || job.Status != JobStatus.Processing)
        {
            _logger.LogWarning("Job {JobId} is not locked by this worker (LockedBy: {LockedBy}, Status: {Status}). Skipping.", 
                jobId, job.LockedBy, job.Status);
            await _redisQueue.RemoveFromProcessingAsync(queue, _workerInfo.Id, jobId);
            return;
        }

        await _uow.Logs.AddLogAsync(jobId, Atlas.Core.Domain.LogLevel.Info, $"Starting execution on worker {_workerInfo.Id}.", cancellationToken: cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        try
        {
            // Execute the handler
            await _handlerRegistry.ExecuteJobAsync(_serviceProvider, jobId, job.JobType, job.Payload, cancellationToken);

            // If execution succeeded
            job.Status = JobStatus.Succeeded;
            job.LockedBy = null;
            job.LockedUntil = null;
            job.UpdatedAt = DateTimeOffset.UtcNow;

            job.StatusHistory.Add(new JobStatusHistory
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                FromStatus = JobStatus.Processing,
                ToStatus = JobStatus.Succeeded,
                Timestamp = DateTimeOffset.UtcNow,
                Notes = "Job executed successfully."
            });

            await _uow.Logs.AddLogAsync(jobId, Atlas.Core.Domain.LogLevel.Info, "Job completed successfully.", cancellationToken: cancellationToken);
            _logger.LogInformation("Job {JobId} executed successfully", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed on attempt {Attempts}", jobId, job.Attempts);

            job.LastError = ex.ToString();
            job.UpdatedAt = DateTimeOffset.UtcNow;

            var retryOptions = _handlerRegistry.GetRetryOptions(job.JobType);

            if (job.Attempts >= retryOptions.MaxAttempts)
            {
                // Dead-letter the job
                job.Status = JobStatus.DeadLettered;
                job.LockedBy = null;
                job.LockedUntil = null;

                job.StatusHistory.Add(new JobStatusHistory
                {
                    Id = Guid.NewGuid(),
                    JobId = jobId,
                    FromStatus = JobStatus.Processing,
                    ToStatus = JobStatus.DeadLettered,
                    Timestamp = DateTimeOffset.UtcNow,
                    Notes = $"Max attempts ({retryOptions.MaxAttempts}) exceeded. Final error: {ex.Message}"
                });

                await _uow.Logs.AddLogAsync(jobId, Atlas.Core.Domain.LogLevel.Error, $"Job dead-lettered. Max attempts ({retryOptions.MaxAttempts}) exceeded.", ex.ToString(), cancellationToken);
                _logger.LogWarning("Job {JobId} dead-lettered after {Attempts} attempts", jobId, job.Attempts);
            }
            else
            {
                // Reschedule for retry
                job.Status = JobStatus.Pending;
                job.LockedBy = null;
                job.LockedUntil = null;

                var delay = RetryPolicy.CalculateDelay(job.Attempts, retryOptions);
                job.ScheduledAt = DateTimeOffset.UtcNow.Add(delay);

                job.StatusHistory.Add(new JobStatusHistory
                {
                    Id = Guid.NewGuid(),
                    JobId = jobId,
                    FromStatus = JobStatus.Processing,
                    ToStatus = JobStatus.Pending,
                    Timestamp = DateTimeOffset.UtcNow,
                    Notes = $"Execution failed. Rescheduled in {delay.TotalSeconds:F1}s (Attempt {job.Attempts}/{retryOptions.MaxAttempts}). Error: {ex.Message}"
                });

                await _uow.Logs.AddLogAsync(jobId, Atlas.Core.Domain.LogLevel.Warning, $"Execution failed. Rescheduled in {delay.TotalSeconds:F1}s.", ex.ToString(), cancellationToken);
                _logger.LogInformation("Job {JobId} rescheduled for retry in {DelaySeconds}s", jobId, delay.TotalSeconds);
            }
        }

        // Save state changes and remove job from Redis processing tracking list
        await _uow.SaveChangesAsync(cancellationToken);
        await _redisQueue.RemoveFromProcessingAsync(queue, _workerInfo.Id, jobId);
    }
}
