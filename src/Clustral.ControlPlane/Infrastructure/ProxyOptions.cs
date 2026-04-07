namespace Clustral.ControlPlane.Infrastructure;

/// <summary>
/// Configuration for the kubectl proxy middleware.
/// Bound from the <c>Proxy</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>
    /// How long to wait for the agent to respond via the gRPC tunnel.
    /// Protects against unresponsive agents — k8s API server timeouts
    /// pass through transparently. Default: 2 minutes.
    /// </summary>
    public TimeSpan TunnelTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Per-credential rate limiting configuration.</summary>
    public RateLimitingOptions RateLimiting { get; set; } = new();
}

/// <summary>
/// Token bucket rate limiting per credential. Defaults match k8s client-go
/// (100 QPS sustained, 200 burst) so the proxy isn't the bottleneck.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>Enable or disable rate limiting entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum burst size (token bucket capacity).</summary>
    public int BurstSize { get; set; } = 200;

    /// <summary>Sustained requests per second (token refill rate).</summary>
    public int RequestsPerSecond { get; set; } = 100;

    /// <summary>Requests queued when bucket is empty before returning 429.</summary>
    public int QueueSize { get; set; } = 50;
}
