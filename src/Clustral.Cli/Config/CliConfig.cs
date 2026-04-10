using System.Text.Json;
using System.Text.Json.Serialization;
using Clustral.Cli.Commands;

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

    /// <summary>Default path: <c>~/.clustral/config.json</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clustral", "config.json");

    /// <summary>
    /// Reads <c>~/.clustral/config.json</c> if it exists, returning
    /// a default-initialised instance otherwise.
    /// </summary>
    public static CliConfig Load() => LoadFrom(DefaultPath);

    /// <summary>Loads configuration from a specific file path. Used by tests.</summary>
    public static CliConfig LoadFrom(string path)
    {
        if (!File.Exists(path))
            return new CliConfig();

        try
        {
            var json = File.ReadAllText(path);
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
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);
        var json = JsonSerializer.Serialize(this, CliJsonContext.Default.CliConfig);
        File.WriteAllText(DefaultPath, json);
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
[JsonSerializable(typeof(ClusterListResponse))]
[JsonSerializable(typeof(UserProfileResponse))]
[JsonSerializable(typeof(UserListResponse))]
[JsonSerializable(typeof(RoleListResponse))]
[JsonSerializable(typeof(AccessRequestCreateRequest))]
[JsonSerializable(typeof(AccessRequestResponse))]
[JsonSerializable(typeof(AccessRequestListResponse))]
[JsonSerializable(typeof(AccessRequestApproveRequest))]
[JsonSerializable(typeof(AccessRequestDenyRequest))]
[JsonSerializable(typeof(AccessRequestRevokeRequest))]
[JsonSerializable(typeof(RevokeByTokenRequest))]
[JsonSerializable(typeof(ConfigShowOutput))]
[JsonSerializable(typeof(StatusOutput))]
[JsonSerializable(typeof(DoctorOutput))]
internal partial class CliJsonContext : JsonSerializerContext { }

// ─────────────────────────────────────────────────────────────────────────────
// `clustral config show --json` output structure
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ConfigShowOutput
{
    [JsonPropertyName("files")]        public ConfigFiles        Files        { get; set; } = new();
    [JsonPropertyName("controlPlane")] public ConfigControlPlane ControlPlane { get; set; } = new();
    [JsonPropertyName("session")]      public ConfigSession      Session      { get; set; } = new();
    [JsonPropertyName("cli")]          public ConfigCli          Cli          { get; set; } = new();
}

internal sealed class ConfigFiles
{
    [JsonPropertyName("config")]     public ConfigFileInfo       Config     { get; set; } = new();
    [JsonPropertyName("token")]      public ConfigFileInfo       Token      { get; set; } = new();
    [JsonPropertyName("kubeconfig")] public ConfigKubeconfigInfo Kubeconfig { get; set; } = new();
}

internal sealed class ConfigFileInfo
{
    [JsonPropertyName("path")]      public string Path      { get; set; } = string.Empty;
    [JsonPropertyName("exists")]    public bool   Exists    { get; set; }
    [JsonPropertyName("sizeBytes")] public long   SizeBytes { get; set; }
}

internal sealed class ConfigKubeconfigInfo
{
    [JsonPropertyName("path")]            public string       Path           { get; set; } = string.Empty;
    [JsonPropertyName("exists")]          public bool         Exists         { get; set; }
    [JsonPropertyName("currentContext")]  public string?      CurrentContext { get; set; }
    [JsonPropertyName("totalContexts")]   public int          TotalContexts  { get; set; }
    [JsonPropertyName("clustralContexts")] public List<string> ClustralContexts { get; set; } = [];
}

internal sealed class ConfigControlPlane
{
    [JsonPropertyName("url")]           public string Url           { get; set; } = string.Empty;
    [JsonPropertyName("oidcAuthority")] public string OidcAuthority { get; set; } = string.Empty;
    [JsonPropertyName("oidcClientId")]  public string OidcClientId  { get; set; } = string.Empty;
    [JsonPropertyName("oidcScopes")]    public string OidcScopes    { get; set; } = string.Empty;
    [JsonPropertyName("insecureTls")]   public bool   InsecureTls   { get; set; }
    [JsonPropertyName("callbackPort")]  public int    CallbackPort  { get; set; }
}

internal sealed class ConfigSession
{
    [JsonPropertyName("status")]          public string          Status          { get; set; } = string.Empty;
    [JsonPropertyName("subject")]         public string?         Subject         { get; set; }
    [JsonPropertyName("issuedAt")]        public DateTimeOffset? IssuedAt        { get; set; }
    [JsonPropertyName("expiresAt")]       public DateTimeOffset? ExpiresAt       { get; set; }
    [JsonPropertyName("validForSeconds")] public long?           ValidForSeconds { get; set; }
}

internal sealed class ConfigCli
{
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
// Wire types for HTTP responses
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Response from <c>GET /.well-known/clustral-configuration</c> (Web UI discovery)
/// or <c>GET /api/v1/config</c> (direct ControlPlane).
/// </summary>
internal sealed class ControlPlaneConfig
{
    [JsonPropertyName("version")]          public string Version         { get; set; } = string.Empty;
    [JsonPropertyName("controlPlaneUrl")]  public string ControlPlaneUrl { get; set; } = string.Empty;
    [JsonPropertyName("oidcAuthority")]    public string OidcAuthority   { get; set; } = string.Empty;
    [JsonPropertyName("oidcClientId")]     public string OidcClientId    { get; set; } = string.Empty;
    [JsonPropertyName("oidcScopes")]       public string OidcScopes      { get; set; } = string.Empty;
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

/// <summary>Mirrors <c>ClusterListResponse</c> in the ControlPlane.</summary>
internal sealed class ClusterListResponse
{
    [JsonPropertyName("clusters")]      public List<ClusterResponse> Clusters      { get; set; } = [];
    [JsonPropertyName("nextPageToken")] public string?               NextPageToken { get; set; }
}

internal sealed class ClusterResponse
{
    [JsonPropertyName("id")]                 public string              Id                { get; set; } = string.Empty;
    [JsonPropertyName("name")]               public string              Name              { get; set; } = string.Empty;
    [JsonPropertyName("description")]        public string              Description       { get; set; } = string.Empty;
    [JsonPropertyName("status")]             public string              Status            { get; set; } = string.Empty;
    [JsonPropertyName("kubernetesVersion")]  public string?             KubernetesVersion { get; set; }
    [JsonPropertyName("agentVersion")]       public string?             AgentVersion      { get; set; }
    [JsonPropertyName("registeredAt")]       public DateTimeOffset      RegisteredAt      { get; set; }
    [JsonPropertyName("lastSeenAt")]         public DateTimeOffset?     LastSeenAt        { get; set; }
    [JsonPropertyName("labels")]             public Dictionary<string, string> Labels     { get; set; } = new();
}

/// <summary>Mirrors <c>UserProfileResponse</c> from GET /api/v1/users/me.</summary>
internal sealed class UserProfileResponse
{
    [JsonPropertyName("id")]           public string               Id           { get; set; } = string.Empty;
    [JsonPropertyName("email")]        public string               Email        { get; set; } = string.Empty;
    [JsonPropertyName("displayName")]  public string?              DisplayName  { get; set; }
    [JsonPropertyName("createdAt")]    public DateTimeOffset        CreatedAt   { get; set; }
    [JsonPropertyName("lastSeenAt")]   public DateTimeOffset?       LastSeenAt  { get; set; }
    [JsonPropertyName("assignments")]  public List<ProfileAssignment> Assignments { get; set; } = [];
    [JsonPropertyName("activeGrants")] public List<ActiveGrant>    ActiveGrants { get; set; } = [];
}

internal sealed class ProfileAssignment
{
    [JsonPropertyName("roleName")]     public string  RoleName     { get; set; } = string.Empty;
    [JsonPropertyName("clusterName")]  public string  ClusterName  { get; set; } = string.Empty;
    [JsonPropertyName("clusterId")]    public string  ClusterId    { get; set; } = string.Empty;
}

internal sealed class ActiveGrant
{
    [JsonPropertyName("requestId")]      public string         RequestId      { get; set; } = string.Empty;
    [JsonPropertyName("roleName")]       public string         RoleName       { get; set; } = string.Empty;
    [JsonPropertyName("clusterId")]      public string         ClusterId      { get; set; } = string.Empty;
    [JsonPropertyName("clusterName")]    public string         ClusterName    { get; set; } = string.Empty;
    [JsonPropertyName("grantExpiresAt")] public DateTimeOffset GrantExpiresAt { get; set; }
}

// ── User listing ────────────────────────────────────────────────────────────

internal sealed class UserListResponse
{
    [JsonPropertyName("users")] public List<UserResponse> Users { get; set; } = [];
}

internal sealed class UserResponse
{
    [JsonPropertyName("id")]          public string         Id          { get; set; } = string.Empty;
    [JsonPropertyName("email")]       public string         Email       { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string?        DisplayName { get; set; }
    [JsonPropertyName("lastSeenAt")]  public DateTimeOffset? LastSeenAt { get; set; }
}

// ── Roles ───────────────────────────────────────────────────────────────────

internal sealed class RoleListResponse
{
    [JsonPropertyName("roles")] public List<RoleResponse> Roles { get; set; } = [];
}

internal sealed class RoleResponse
{
    [JsonPropertyName("id")]               public string       Id               { get; set; } = string.Empty;
    [JsonPropertyName("name")]             public string       Name             { get; set; } = string.Empty;
    [JsonPropertyName("description")]      public string       Description      { get; set; } = string.Empty;
    [JsonPropertyName("kubernetesGroups")] public List<string> KubernetesGroups { get; set; } = [];
    [JsonPropertyName("createdAt")]        public DateTimeOffset CreatedAt      { get; set; }
}

// ── Access requests ─────────────────────────────────────────────────────────

internal sealed class AccessRequestCreateRequest
{
    [JsonPropertyName("roleId")]                  public string       RoleId                  { get; set; } = string.Empty;
    [JsonPropertyName("clusterId")]               public string       ClusterId               { get; set; } = string.Empty;
    [JsonPropertyName("reason")]                  public string?      Reason                  { get; set; }
    [JsonPropertyName("requestedDuration")]       public string?      RequestedDuration       { get; set; }
    [JsonPropertyName("suggestedReviewerEmails")] public List<string>? SuggestedReviewerEmails { get; set; }
}

internal sealed class AccessRequestResponse
{
    [JsonPropertyName("id")]                  public string         Id                  { get; set; } = string.Empty;
    [JsonPropertyName("requesterId")]         public string         RequesterId         { get; set; } = string.Empty;
    [JsonPropertyName("requesterEmail")]      public string         RequesterEmail      { get; set; } = string.Empty;
    [JsonPropertyName("requesterDisplayName")]public string?        RequesterDisplayName { get; set; }
    [JsonPropertyName("roleId")]              public string         RoleId              { get; set; } = string.Empty;
    [JsonPropertyName("roleName")]            public string         RoleName            { get; set; } = string.Empty;
    [JsonPropertyName("clusterId")]           public string         ClusterId           { get; set; } = string.Empty;
    [JsonPropertyName("clusterName")]         public string         ClusterName         { get; set; } = string.Empty;
    [JsonPropertyName("status")]              public string         Status              { get; set; } = string.Empty;
    [JsonPropertyName("reason")]              public string         Reason              { get; set; } = string.Empty;
    [JsonPropertyName("requestedDuration")]   public string         RequestedDuration   { get; set; } = string.Empty;
    [JsonPropertyName("createdAt")]           public DateTimeOffset CreatedAt           { get; set; }
    [JsonPropertyName("requestExpiresAt")]    public DateTimeOffset RequestExpiresAt    { get; set; }
    [JsonPropertyName("reviewerId")]          public string?        ReviewerId          { get; set; }
    [JsonPropertyName("reviewerEmail")]       public string?        ReviewerEmail       { get; set; }
    [JsonPropertyName("reviewedAt")]          public DateTimeOffset? ReviewedAt         { get; set; }
    [JsonPropertyName("denialReason")]        public string?        DenialReason        { get; set; }
    [JsonPropertyName("grantExpiresAt")]      public DateTimeOffset? GrantExpiresAt     { get; set; }
    [JsonPropertyName("revokedAt")]           public DateTimeOffset? RevokedAt          { get; set; }
    [JsonPropertyName("revokedByEmail")]      public string?        RevokedByEmail      { get; set; }
    [JsonPropertyName("revokedReason")]       public string?        RevokedReason       { get; set; }
}

internal sealed class AccessRequestListResponse
{
    [JsonPropertyName("requests")] public List<AccessRequestResponse> Requests { get; set; } = [];
}

internal sealed class AccessRequestApproveRequest
{
    [JsonPropertyName("durationOverride")] public string? DurationOverride { get; set; }
}

internal sealed class AccessRequestDenyRequest
{
    [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
}

internal sealed class AccessRequestRevokeRequest
{
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

internal sealed class RevokeByTokenRequest
{
    [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
}
