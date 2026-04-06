namespace Clustral.Sdk.Kubeconfig;

/// <summary>
/// Kubeconfig entry using client certificate authentication.
/// Supports both file-path and embedded (base64) certificate data.
/// </summary>
public sealed record CertificateKubeconfigEntry(
    string ContextName,
    string ServerUrl,
    string? ClientCertificatePath = null,
    string? ClientCertificateData = null,
    string? ClientKeyPath = null,
    string? ClientKeyData = null,
    string? CertificateAuthorityPath = null,
    string? CertificateAuthorityData = null,
    string? Namespace = null,
    bool InsecureSkipTlsVerify = false);

/// <summary>
/// Kubeconfig entry using exec-based credential plugin authentication.
/// This is the preferred pattern for enterprise human access.
/// </summary>
public sealed record ExecKubeconfigEntry(
    string ContextName,
    string ServerUrl,
    string ExecCommand,
    string ExecApiVersion = "client.authentication.k8s.io/v1beta1",
    string[]? ExecArgs = null,
    Dictionary<string, string>? ExecEnv = null,
    string? InstallHint = null,
    string? CertificateAuthorityPath = null,
    string? CertificateAuthorityData = null,
    string? Namespace = null,
    bool InsecureSkipTlsVerify = false);
