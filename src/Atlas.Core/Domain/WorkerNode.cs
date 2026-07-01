using System;

namespace Atlas.Core.Domain;

public class WorkerNode
{
    public string Id { get; set; } = string.Empty;
    public WorkerStatus Status { get; set; }
    public DateTimeOffset LastHeartbeat { get; set; }
    public int ConcurrencyLimit { get; set; }
    public int ActiveJobs { get; set; }
}
