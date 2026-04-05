using System.ComponentModel.DataAnnotations;

namespace Clustral.ControlPlane.Api.Models;

// ─────────────────────────────────────────────────────────────────────────────
// POST /api/v1/auth/kubeconfig-credential
// ─────────────────────────────────────────────────────────────────────────────

public sealed record IssueKubeconfigCredentialRequest(
    [Required] Guid   ClusterId,
    /// <summary>
    /// Optional. ISO 8601 duration string (e.g. "PT4H"). The server caps this
    /// at <c>Keycloak:MaxKubeconfigCredentialTtl</c>.
    /// </summary>
    string? RequestedTtl = null);

public sealed record IssueKubeconfigCredentialResponse(
    Guid           CredentialId,
    string         Token,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string         Subject,
    string?        DisplayName);

// ─────────────────────────────────────────────────────────────────────────────
// DELETE /api/v1/auth/credentials/{id}
// ─────────────────────────────────────────────────────────────────────────────

public sealed record RevokeCredentialRequest(string? Reason = null);

// ─────────────────────────────────────────────────────────────────────────────
// POST /api/v1/auth/revoke-by-token
// ─────────────────────────────────────────────────────────────────────────────

public sealed record RevokeByTokenRequest([Required] string Token);

public sealed record RevokeCredentialResponse(
    bool           Revoked,
    DateTimeOffset RevokedAt);
