using System.ComponentModel.DataAnnotations;

namespace Clustral.Agent;

/// <summary>
/// Typed configuration for the Clustral Agent.
/// Bound from the <c>Agent</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>
    /// Cluster ID assigned by the ControlPlane at registration time.
    /// Required after first boot.
    /// </summary>
    [Required]
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// gRPC endpoint of the ControlPlane (e.g. <c>https://cp.example.com:5001</c>).
    /// Use <c>http://</c> for local dev (kind / docker-compose).
    /// </summary>
    [Required]
    public string ControlPlaneUrl { get; set; } = string.Empty;

    /// <summary>
    /// Path to the persisted agent credential (long-lived bearer token).
    /// Written on first boot after <c>AuthService.IssueAgentCredential</c>.
    /// Defaults to <c>~/.clustral/agent.token</c> (local dev) or
    /// <c>/etc/clustral/agent.token</c> (in-cluster, override via env var).
    /// </summary>
    public string CredentialPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clustral", "agent.token");

    /// <summary>
    /// One-time bootstrap token from <c>ClusterService.Register</c>.
    /// Read at startup when no credential file exists; cleared conceptually
    /// after the agent credential is issued.
    /// Supply via environment variable <c>Agent__BootstrapToken</c> in k8s.
    /// </summary>
    public string BootstrapToken { get; set; } = string.Empty;

    /// <summary>
    /// PEM-encoded Ed25519 public key of this agent.
    /// Must match what was submitted at registration.
    /// </summary>
    public string AgentPublicKeyPem { get; set; } = string.Empty;

    /// <summary>Semantic version of this agent binary (e.g. <c>0.1.0</c>).</summary>
    public string AgentVersion { get; set; } = "0.1.0";

    /// <summary>
    /// Base URL of the Kubernetes API server.
    /// Defaults to the in-cluster address; override for local dev.
    /// </summary>
    public string KubernetesApiUrl { get; set; } = "https://kubernetes.default.svc";

    /// <summary>
    /// Set <c>true</c> to skip TLS verification of the Kubernetes API server.
    /// Use only in local dev with kind clusters.
    /// </summary>
    public bool KubernetesSkipTlsVerify { get; set; }

    /// <summary>How often to send <c>ClusterService.UpdateStatus</c> heartbeats.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How close to expiry an agent credential must be before rotation is
    /// attempted on startup.
    /// </summary>
    public TimeSpan CredentialRotationThreshold { get; set; } = TimeSpan.FromDays(30);

    public ReconnectOptions Reconnect { get; set; } = new();
}

public sealed class ReconnectOptions
{
    /// <summary>Initial delay before the first reconnect attempt.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound on reconnect delay after successive failures.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Multiplier applied to the delay after each failed attempt.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum random jitter added to each delay to prevent thundering herd
    /// when many agents reconnect simultaneously.
    /// </summary>
    public TimeSpan MaxJitter { get; set; } = TimeSpan.FromSeconds(5);
}
