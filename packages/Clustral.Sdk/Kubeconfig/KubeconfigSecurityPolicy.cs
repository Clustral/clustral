namespace Clustral.Sdk.Kubeconfig;

/// <summary>
/// Configurable security rules for kubeconfig validation.
/// Each rule, when enabled, causes validation to report an error
/// if the corresponding pattern is found.
/// </summary>
public sealed class KubeconfigSecurityPolicy
{
    /// <summary>
    /// Reject <c>insecure-skip-tls-verify: true</c> on any cluster.
    /// </summary>
    public bool ForbidInsecureSkipTls { get; init; }

    /// <summary>
    /// Reject cluster server URLs that use <c>http://</c> instead of <c>https://</c>.
    /// </summary>
    public bool ForbidPlaintextServer { get; init; }

    /// <summary>
    /// Reject static <c>password</c> fields on user entries.
    /// </summary>
    public bool ForbidStaticPasswords { get; init; }

    /// <summary>
    /// Reject static <c>token</c> fields on user entries when a security
    /// policy requires exec-based or cert-based auth for human access.
    /// </summary>
    public bool ForbidStaticTokens { get; init; }

    /// <summary>
    /// Require every context to have a <c>namespace</c> set.
    /// </summary>
    public bool RequireNamespaceOnContexts { get; init; }

    /// <summary>A policy that allows everything (default).</summary>
    public static KubeconfigSecurityPolicy Permissive => new();

    /// <summary>A strict enterprise policy that forbids insecure patterns.</summary>
    public static KubeconfigSecurityPolicy Strict => new()
    {
        ForbidInsecureSkipTls = true,
        ForbidPlaintextServer = true,
        ForbidStaticPasswords = true,
        ForbidStaticTokens = true,
    };
}
