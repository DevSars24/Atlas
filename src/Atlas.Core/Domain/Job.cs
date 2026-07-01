using System;
using System.Collections.Generic;

namespace Atlas.Core.Domain;

public class Job
{
    public Guid Id { get; set; }
    public string Queue { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public JobStatus Status { get; set; }
    public string Payload { get; set; } = "{}";
    public JobPriority Priority { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? LastError { get; set; }

    public ICollection<JobStatusHistory> StatusHistory { get; set; } = new List<JobStatusHistory>();
    public ICollection<JobLog> Logs { get; set; } = new List<JobLog>();
}
