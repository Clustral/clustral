using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clustral.Cli.Config;

/// <summary>
/// User-level CLI configuration persisted at <c>~/.clustral/config.json</c>.
/// Values here are the defaults; every field can be overridden at the
/// command line.
/// </summary>
public sealed class CliConfig
{
    /// <summary>
    /// Keycloak realm URL (e.g. <c>http://localhost:8080/realms/clustral</c>).
    /// </summary>
    public string OidcAuthority { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 public client ID registered in Keycloak for device / PKCE flows.
    /// </summary>
    public string OidcClientId { get; set; } = "clustral-cli";

    /// <summary>
    /// Space-separated OIDC scopes to request.
    /// </summary>
    public string OidcScopes { get; set; } = "openid email profile";

    /// <summary>
    /// Clustral ControlPlane base URL (e.g. <c>https://cp.example.com</c>).
    /// </summary>
    public string ControlPlaneUrl { get; set; } = string.Empty;

    /// <summary>
    /// Local TCP port the OIDC callback server will listen on.
    /// The redirect URI registered in Keycloak must match
    /// <c>http://127.0.0.1:{CallbackPort}/callback</c>.
    /// </summary>
    public int CallbackPort { get; set; } = 7777;

    /// <summary>
    /// Skip TLS verification for all outbound HTTPS calls.
    /// Must be <c>false</c> in production.
    /// </summary>
    public bool InsecureTls { get; set; } = false;

    // ─────────────────────────────────────────────────────────────────────────

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clustral", "config.json");

    /// <summary>
    /// Reads <c>~/.clustral/config.json</c> if it exists, returning
    /// a default-initialised instance otherwise.
    /// </summary>
    public static CliConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new CliConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize(json, CliJsonContext.Default.CliConfig)
                   ?? new CliConfig();
        }
        catch
        {
            // Corrupt config — return defaults and let the user fix it.
            return new CliConfig();
        }
    }

    /// <summary>Persists the current config to <c>~/.clustral/config.json</c>.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var json = JsonSerializer.Serialize(this, CliJsonContext.Default.CliConfig);
        File.WriteAllText(ConfigPath, json);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AOT-safe JSON source-generation context.
// Every type that is serialised / deserialised in the CLI must be listed here.
// ─────────────────────────────────────────────────────────────────────────────

[JsonSourceGenerationOptions(
    PropertyNamingPolicy    = JsonKnownNamingPolicy.CamelCase,
    WriteIndented           = true,
    DefaultIgnoreCondition  = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliConfig))]
[JsonSerializable(typeof(KeycloakTokenResponse))]
[JsonSerializable(typeof(IssueCredentialRequest))]
[JsonSerializable(typeof(IssueCredentialResponse))]
[JsonSerializable(typeof(ControlPlaneConfig))]
internal partial class CliJsonContext : JsonSerializerContext { }

// ─────────────────────────────────────────────────────────────────────────────
// Wire types for HTTP responses
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Response from <c>GET /api/v1/config</c> on the ControlPlane.</summary>
internal sealed class ControlPlaneConfig
{
    [JsonPropertyName("oidcAuthority")] public string OidcAuthority { get; set; } = string.Empty;
    [JsonPropertyName("oidcClientId")]  public string OidcClientId  { get; set; } = string.Empty;
    [JsonPropertyName("oidcScopes")]    public string OidcScopes    { get; set; } = string.Empty;
}

internal sealed class KeycloakTokenResponse
{
    [JsonPropertyName("access_token")]  public string AccessToken  { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")]    public int    ExpiresIn    { get; set; }
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("token_type")]    public string TokenType    { get; set; } = string.Empty;
    [JsonPropertyName("scope")]         public string Scope        { get; set; } = string.Empty;
}

/// <summary>Mirrors <c>IssueKubeconfigCredentialRequest</c> in the ControlPlane.</summary>
internal sealed class IssueCredentialRequest
{
    [JsonPropertyName("clusterId")]    public string  ClusterId    { get; set; } = string.Empty;
    [JsonPropertyName("requestedTtl")] public string? RequestedTtl { get; set; }
}

/// <summary>Mirrors <c>IssueKubeconfigCredentialResponse</c> in the ControlPlane.</summary>
internal sealed class IssueCredentialResponse
{
    [JsonPropertyName("credentialId")] public string        CredentialId { get; set; } = string.Empty;
    [JsonPropertyName("token")]        public string        Token        { get; set; } = string.Empty;
    [JsonPropertyName("issuedAt")]     public DateTimeOffset IssuedAt   { get; set; }
    [JsonPropertyName("expiresAt")]    public DateTimeOffset ExpiresAt  { get; set; }
    [JsonPropertyName("subject")]      public string        Subject      { get; set; } = string.Empty;
    [JsonPropertyName("displayName")]  public string?       DisplayName  { get; set; }
}
