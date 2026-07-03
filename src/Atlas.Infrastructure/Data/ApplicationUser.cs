using Microsoft.AspNetCore.Identity;

namespace Atlas.Infrastructure.Data;

/// <summary>
/// Atlas user for JWT dashboard login. Extends ASP.NET Core Identity.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Role: Admin | Operator | Viewer</summary>
    public string Role { get; set; } = "Viewer";
}
