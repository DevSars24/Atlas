using System;
using System.Threading.Tasks;
using Atlas.Api.Hubs;
using Atlas.Core.Domain;
using Microsoft.AspNetCore.SignalR;

namespace Atlas.Api.Services;

/// <summary>
/// Pushes job log entries and status changes to connected SignalR clients.
/// </summary>
public class JobHubNotifier
{
    private readonly IHubContext<JobLogHub> _hub;

    public JobHubNotifier(IHubContext<JobLogHub> hub)
    {
        _hub = hub;
    }

    public async Task SendLogAsync(Guid jobId, string level, string message, DateTimeOffset timestamp)
    {
        var groupName = $"job:{jobId}";
        await _hub.Clients.Group(groupName).SendAsync("ReceiveLog", new
        {
            JobId = jobId,
            Level = level,
            Message = message,
            Timestamp = timestamp
        });
    }

    public async Task SendStatusUpdateAsync(Guid jobId, string status)
    {
        // Notify job-specific group
        await _hub.Clients.Group($"job:{jobId}").SendAsync("JobStatusChanged", new
        {
            JobId = jobId,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow
        });

        // Notify overview group
        await _hub.Clients.Group("all-jobs").SendAsync("JobStatusChanged", new
        {
            JobId = jobId,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
