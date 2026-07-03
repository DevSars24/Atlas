using System;

namespace Atlas.Core.Domain;

public class ApiKey
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;   // bcrypt hash of the raw key
    public string Role { get; set; } = "Viewer";           // Admin | Operator | Viewer
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
