using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Atlas.Api.Services;

/// <summary>
/// Background service that listens to the Redis pub/sub logs channel and broadcasts them to the SignalR clients.
/// </summary>
public class RedisLogSubscriberService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IHubContext<JobLogHub> _hubContext;
    private readonly ILogger<RedisLogSubscriberService> _logger;

    public RedisLogSubscriberService(
        IConnectionMultiplexer redis,
        IHubContext<JobLogHub> hubContext,
        ILogger<RedisLogSubscriberService> logger)
    {
        _redis = redis;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RedisLogSubscriberService is starting, subscribing to 'atlas:job:logs' channel.");

        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync("atlas:job:logs", async (channel, message) =>
        {
            try
            {
                var payload = message.ToString();
                if (string.IsNullOrWhiteSpace(payload)) return;

                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                var jobIdStr = root.GetProperty("JobId").GetString();
                if (string.IsNullOrEmpty(jobIdStr) || !Guid.TryParse(jobIdStr, out var jobId)) return;

                var level = root.GetProperty("Level").GetString() ?? "Info";
                var msg = root.GetProperty("Message").GetString() ?? string.Empty;
                var timestampStr = root.GetProperty("Timestamp").GetString();
                var timestamp = DateTimeOffset.TryParse(timestampStr, out var ts) ? ts : DateTimeOffset.UtcNow;

                // Broadcast to the job-specific group
                var groupName = $"job:{jobId}";
                await _hubContext.Clients.Group(groupName).SendAsync("ReceiveLog", new
                {
                    JobId = jobId,
                    Level = level,
                    Message = msg,
                    Timestamp = timestamp
                }, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Redis log pub/sub message.");
            }
        });

        // Keep running until stopped
        var tcs = new TaskCompletionSource();
        using (stoppingToken.Register(() => tcs.SetResult()))
        {
            await tcs.Task;
        }

        _logger.LogInformation("RedisLogSubscriberService is stopping, unsubscribing from 'atlas:job:logs'.");
        await subscriber.UnsubscribeAsync("atlas:job:logs");
    }
}
