using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Atlas.Api.Hubs;

/// <summary>
/// SignalR hub for real-time job log streaming and status updates.
/// Clients subscribe to a group named after the JobId.
/// </summary>
public class JobLogHub : Hub
{
    /// <summary>
    /// Subscribe to live updates for a specific job.
    /// </summary>
    public async Task SubscribeToJob(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"job:{jobId}");
    }

    /// <summary>
    /// Unsubscribe from a specific job's updates.
    /// </summary>
    public async Task UnsubscribeFromJob(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job:{jobId}");
    }

    /// <summary>
    /// Subscribe to all job status updates (for the dashboard overview).
    /// </summary>
    public async Task SubscribeToAll()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-jobs");
    }
}
