using System;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Worker.Services;

public class HeartbeatService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkerInfo _workerInfo;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(
        IServiceProvider serviceProvider,
        WorkerInfo workerInfo,
        ILogger<HeartbeatService> logger)
    {
        _serviceProvider = serviceProvider;
        _workerInfo = workerInfo;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting HeartbeatService with 15s interval");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                await uow.Workers.AddOrUpdateHeartbeatAsync(_workerInfo.Id, _workerInfo.ConcurrencyLimit, _workerInfo.ActiveJobs, stoppingToken);
                await uow.SaveChangesAsync(stoppingToken);

                _logger.LogDebug("Heartbeat sent successfully. Active jobs: {ActiveJobs}", _workerInfo.ActiveJobs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send heartbeat for worker {WorkerId}", _workerInfo.Id);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("HeartbeatService stopped");
    }
}
