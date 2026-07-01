using System;

namespace Atlas.Core.Domain;

public class WorkflowDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public class WorkflowRun
{
    public Guid Id { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    public WorkflowDefinition? WorkflowDefinition { get; set; }
}
