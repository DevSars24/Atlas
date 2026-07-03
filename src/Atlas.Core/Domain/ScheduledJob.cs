using System;

namespace Atlas.Core.Domain;

public enum MisfirePolicy
{
    Skip = 0,        // Skip missed executions
    RunOnce = 1      // Run once immediately if missed
}

public class ScheduledJob
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public string Queue { get; set; } = "default";
    public JobPriority Priority { get; set; } = JobPriority.Normal;
    public int MaxAttempts { get; set; } = 3;
    public bool IsEnabled { get; set; } = true;
    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.Skip;
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
