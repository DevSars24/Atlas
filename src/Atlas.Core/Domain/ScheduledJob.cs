using System;

namespace Atlas.Core.Domain;

public class ScheduledJob
{
    public Guid Id { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public string Queue { get; set; } = "default";
    public JobPriority Priority { get; set; }
}
