using System;

namespace Atlas.Core.Domain;

public class JobLog
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public LogLevel LogLevel { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string? Exception { get; set; }

    public Job? Job { get; set; }
}
