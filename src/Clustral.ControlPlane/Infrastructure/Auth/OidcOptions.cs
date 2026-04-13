namespace Clustral.ControlPlane.Infrastructure.Auth;

/// <summary>
/// Typed options for kubeconfig credential issuance.
/// Bound from the <c>Credential</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class CredentialOptions
{
    public const string SectionName = "Credential";

    /// <summary>
    /// Default lifetime granted when the caller does not request a specific TTL.
    /// </summary>
    public TimeSpan DefaultKubeconfigCredentialTtl { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Maximum lifetime that can be requested by the caller.</summary>
    public TimeSpan MaxKubeconfigCredentialTtl { get; set; } = TimeSpan.FromHours(8);
}
