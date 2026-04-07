namespace Clustral.ControlPlane.Domain.Services;

/// <summary>
/// Result of proxy credential authentication. Identifies the user and
/// the credential used.
/// </summary>
public sealed record ProxyIdentity(Guid UserId, Guid ClusterId, Guid CredentialId);

/// <summary>
/// Resolved impersonation identity for the k8s API server.
/// </summary>
public sealed record ImpersonationIdentity(string User, IReadOnlyList<string> Groups);
