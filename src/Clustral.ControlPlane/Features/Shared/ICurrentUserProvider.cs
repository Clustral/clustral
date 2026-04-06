using Clustral.ControlPlane.Domain;

namespace Clustral.ControlPlane.Features.Shared;

/// <summary>
/// Abstracts current user resolution from HTTP context.
/// Scoped to the current request.
/// </summary>
public interface ICurrentUserProvider
{
    string? Subject { get; }
    string? Email { get; }
    string? DisplayName { get; }
    Task<User?> GetCurrentUserAsync(CancellationToken ct);
}
