using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Atlas.Infrastructure.Data;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Api.Auth;

public class ApiKeyAuthOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Validates the X-Api-Key header against bcrypt-hashed keys in the database.
/// </summary>
public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private readonly AtlasDbContext _db;

    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AtlasDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
        {
            return AuthenticateResult.NoResult();
        }

        var key = rawKey.ToString();

        // Load all active API keys and verify against bcrypt hash
        var apiKeys = await _db.ApiKeys
            .Where(k => k.IsActive)
            .ToListAsync();

        var matchedKey = apiKeys.FirstOrDefault(k =>
        {
            try { return BCrypt.Net.BCrypt.Verify(key, k.KeyHash); }
            catch { return false; }
        });

        if (matchedKey == null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        // Update LastUsedAt (fire-and-forget style to avoid blocking)
        matchedKey.LastUsedAt = DateTimeOffset.UtcNow;
        _db.ApiKeys.Update(matchedKey);
        await _db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, matchedKey.Name),
            new Claim(ClaimTypes.Role, matchedKey.Role),
            new Claim("api_key_id", matchedKey.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
