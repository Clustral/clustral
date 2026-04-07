using Clustral.ControlPlane.Infrastructure;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Domain.Services;

/// <summary>
/// Domain service that centralizes OIDC user upsert logic. Previously
/// duplicated in <c>UserSyncFilter</c> and <c>IssueKubeconfigCredential</c>.
/// </summary>
public sealed class UserSyncService(ClustralDb db)
{
    /// <summary>
    /// Creates or updates a user based on OIDC claims. Returns the
    /// persisted user entity.
    /// </summary>
    public async Task<User> SyncFromOidcClaimsAsync(
        string subject, string? email, string? displayName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);

        var existing = await db.Users
            .Find(u => u.KeycloakSubject == subject)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                KeycloakSubject = subject,
                DisplayName = displayName,
                Email = email,
            };
            await db.Users.InsertOneAsync(user, cancellationToken: ct);
            return user;
        }

        existing.UpdateFromOidcClaims(email, displayName);
        await db.Users.ReplaceOneAsync(u => u.Id == existing.Id, existing, cancellationToken: ct);
        return existing;
    }
}
