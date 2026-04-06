namespace Clustral.ControlPlane.Infrastructure.Auth;

/// <summary>
/// Typed options for the OIDC provider integration (Keycloak, Auth0, Okta, etc.).
/// Bound from the <c>Oidc</c> section in <c>appsettings.json</c>.
/// Falls back to the legacy <c>Keycloak</c> section for backward compatibility.
/// </summary>
public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    /// <summary>
    /// OIDC authority URL — the full realm URL, e.g.
    /// <c>http://localhost:8080/realms/clustral</c>.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 client ID registered in the OIDC provider for the control-plane API.
    /// Used as the expected audience when validating inbound JWTs.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Expected <c>aud</c> claim value.  Defaults to <see cref="ClientId"/>
    /// when empty, which matches most OIDC providers' standard JWT shape.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Set to <c>false</c> in local dev (OIDC provider running over plain HTTP).
    /// Must be <c>true</c> in production.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Optional override for the OIDC discovery endpoint.  When running in
    /// Docker Compose the browser token issuer is <c>localhost:8080</c> but the
    /// ControlPlane container must fetch JWKS from <c>keycloak:8080</c>.
    /// Set this to <c>http://keycloak:8080/realms/clustral/.well-known/openid-configuration</c>
    /// in that scenario.  Leave empty to derive from <see cref="Authority"/>.
    /// </summary>
    public string MetadataAddress { get; set; } = string.Empty;

    /// <summary>
    /// Default lifetime granted when the caller does not request a specific TTL.
    /// </summary>
    public TimeSpan DefaultKubeconfigCredentialTtl { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Maximum lifetime that can be requested by the caller.</summary>
    public TimeSpan MaxKubeconfigCredentialTtl { get; set; } = TimeSpan.FromHours(8);
}
