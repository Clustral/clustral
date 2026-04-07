using Clustral.ControlPlane.Domain.Repositories;

namespace Clustral.ControlPlane.Domain.Services;

/// <summary>
/// Domain service that centralizes OIDC user upsert logic. Previously
/// duplicated in <c>UserSyncFilter</c> and <c>IssueKubeconfigCredential</c>.
/// </summary>
public sealed class UserSyncService(IUserRepository users)
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

        var existing = await users.GetBySubjectAsync(subject, ct);

        if (existing is null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                KeycloakSubject = subject,
                DisplayName = displayName,
                Email = email,
            };
            await users.InsertAsync(user, ct);
            return user;
        }

        existing.UpdateFromOidcClaims(email, displayName);
        await users.ReplaceAsync(existing, ct);
        return existing;
    }
}
