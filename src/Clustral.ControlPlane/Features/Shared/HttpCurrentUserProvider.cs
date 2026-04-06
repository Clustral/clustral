using System.Security.Claims;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Shared;

/// <summary>
/// Resolves the current user from HTTP context claims and the MongoDB users collection.
/// Registered as scoped — one instance per request.
/// </summary>
public sealed class HttpCurrentUserProvider(
    IHttpContextAccessor httpContextAccessor,
    ClustralDb db) : ICurrentUserProvider
{
    private User? _cachedUser;
    private bool _resolved;

    public string? Subject =>
        httpContextAccessor.HttpContext?.User.Claims
            .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
        ?? httpContextAccessor.HttpContext?.User.Claims
            .FirstOrDefault(c => c.Type == "sub")?.Value;

    public string? Email =>
        httpContextAccessor.HttpContext?.User.Claims
            .FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
        ?? httpContextAccessor.HttpContext?.User.Claims
            .FirstOrDefault(c => c.Type == "email")?.Value;

    public string? DisplayName =>
        httpContextAccessor.HttpContext?.User.Claims
            .FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
        ?? httpContextAccessor.HttpContext?.User.Claims
            .FirstOrDefault(c => c.Type == "name")?.Value;

    public async Task<User?> GetCurrentUserAsync(CancellationToken ct)
    {
        if (_resolved) return _cachedUser;

        var subject = Subject;
        if (string.IsNullOrEmpty(subject))
        {
            _resolved = true;
            return null;
        }

        _cachedUser = await db.Users
            .Find(u => u.KeycloakSubject == subject)
            .FirstOrDefaultAsync(ct);

        _resolved = true;
        return _cachedUser;
    }
}
