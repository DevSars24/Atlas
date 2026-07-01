using System;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Worker.Services;

public class WorkerInfo
{
    private int _activeJobs;

    public string Id { get; }
    public int ConcurrencyLimit { get; }
    public int ActiveJobs => _activeJobs;

    public WorkerInfo(string? id = null, int concurrencyLimit = 5)
    {
        Id = string.IsNullOrWhiteSpace(id) 
            ? $"worker-{Guid.NewGuid():N}" 
            : id;
        ConcurrencyLimit = concurrencyLimit;
    }

    public int IncrementActiveJobs() => Interlocked.Increment(ref _activeJobs);
    public int DecrementActiveJobs() => Interlocked.Decrement(ref _activeJobs);
}

public class WorkerRegistrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkerInfo _workerInfo;
    private readonly ILogger<WorkerRegistrationService> _logger;

    public WorkerRegistrationService(
        IServiceProvider serviceProvider,
        WorkerInfo workerInfo,
        ILogger<WorkerRegistrationService> _logger)
    {
        _serviceProvider = serviceProvider;
        _workerInfo = workerInfo;
        this._logger = _logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Registering worker node {WorkerId} with concurrency limit {Limit}", 
            _workerInfo.Id, _workerInfo.ConcurrencyLimit);

        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.Workers.AddOrUpdateHeartbeatAsync(_workerInfo.Id, _workerInfo.ConcurrencyLimit, 0, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Worker node {WorkerId} registered successfully", _workerInfo.Id);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("De-registering worker node {WorkerId}", _workerInfo.Id);

        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var worker = await uow.Workers.GetByIdAsync(_workerInfo.Id, cancellationToken);
        if (worker != null)
        {
            worker.Status = WorkerStatus.Inactive;
            await uow.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Worker node {WorkerId} de-registered (status set to Inactive)", _workerInfo.Id);
    }
}
