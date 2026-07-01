using System;

namespace Atlas.Core.Domain;

public class JobStatusHistory
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public JobStatus FromStatus { get; set; }
    public JobStatus ToStatus { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Notes { get; set; }

    public Job? Job { get; set; }
}
