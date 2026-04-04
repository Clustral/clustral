using YamlDotNet.Serialization;

namespace Clustral.Sdk.Kubeconfig;

// ─────────────────────────────────────────────────────────────────────────────
// Typed model for ~/.kube/config.
//
// Only the fields Clustral needs to read and write are modelled here.
// Unknown top-level keys are preserved as-is by YamlDotNet's
// IgnoreUnmatchedProperties() setting on the deserializer.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class KubeconfigDocument
{
    [YamlMember(Alias = "apiVersion")]
    public string ApiVersion { get; set; } = "v1";

    [YamlMember(Alias = "kind")]
    public string Kind { get; set; } = "Config";

    [YamlMember(Alias = "preferences")]
    public Dictionary<object, object> Preferences { get; set; } = new();

    [YamlMember(Alias = "clusters")]
    public List<NamedCluster> Clusters { get; set; } = [];

    [YamlMember(Alias = "users")]
    public List<NamedUser> Users { get; set; } = [];

    [YamlMember(Alias = "contexts")]
    public List<NamedContext> Contexts { get; set; } = [];

    [YamlMember(Alias = "current-context")]
    public string CurrentContext { get; set; } = string.Empty;
}

public sealed class NamedCluster
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "cluster")]
    public ClusterData Cluster { get; set; } = new();
}

public sealed class ClusterData
{
    [YamlMember(Alias = "server")]
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// Set to <c>true</c> only for local dev clusters (kind).
    /// Never set this in production configurations.
    /// </summary>
    [YamlMember(Alias = "insecure-skip-tls-verify")]
    public bool InsecureSkipTlsVerify { get; set; }
}

public sealed class NamedUser
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "user")]
    public UserData User { get; set; } = new();
}

public sealed class UserData
{
    /// <summary>
    /// Short-lived Clustral kubeconfig bearer token issued by
    /// <c>AuthService.IssueKubeconfigCredential</c>.
    /// </summary>
    [YamlMember(Alias = "token")]
    public string Token { get; set; } = string.Empty;
}

public sealed class NamedContext
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "context")]
    public ContextData Context { get; set; } = new();
}

public sealed class ContextData
{
    [YamlMember(Alias = "cluster")]
    public string Cluster { get; set; } = string.Empty;

    [YamlMember(Alias = "user")]
    public string User { get; set; } = string.Empty;

    [YamlMember(Alias = "namespace")]
    public string? Namespace { get; set; }
}
