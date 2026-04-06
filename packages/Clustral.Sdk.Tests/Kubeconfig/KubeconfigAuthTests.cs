using Clustral.Sdk.Kubeconfig;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Categories 11, 12: authentication mode coverage and exec auth formatting.
/// </summary>
public sealed class KubeconfigAuthTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;
    private readonly KubeconfigWriter _sut;

    public KubeconfigAuthTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-auth-{Guid.NewGuid():N}");
        _kubeconfigPath = Path.Combine(_tempDir, "config");
        _sut = new KubeconfigWriter(_kubeconfigPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Token auth (existing) ───────────────────────────────────────────────

    [Fact]
    public void TokenAuth_EmitsTokenField()
    {
        _sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-prod", "https://cp.example.com", "my-jwt",
            DateTimeOffset.UtcNow.AddHours(8)));

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("token: my-jwt", yaml);
    }

    // ── Certificate auth ────────────────────────────────────────────────────

    [Fact]
    public void CertAuth_PathBased_EmitsCorrectFields()
    {
        var entry = new CertificateKubeconfigEntry(
            ContextName: "cert-prod",
            ServerUrl: "https://k8s.example.com:6443",
            ClientCertificatePath: "/home/user/.certs/client.crt",
            ClientKeyPath: "/home/user/.certs/client.key",
            CertificateAuthorityPath: "/home/user/.certs/ca.crt");

        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("client-certificate: /home/user/.certs/client.crt", yaml);
        Assert.Contains("client-key: /home/user/.certs/client.key", yaml);
        Assert.Contains("certificate-authority: /home/user/.certs/ca.crt", yaml);
    }

    [Fact]
    public void CertAuth_EmbeddedData_EmitsBase64Fields()
    {
        var entry = new CertificateKubeconfigEntry(
            ContextName: "cert-embedded",
            ServerUrl: "https://k8s.example.com:6443",
            ClientCertificateData: "LS0tLS1CRUdJTiBDRVJU",
            ClientKeyData: "LS0tLS1CRUdJTiBSU0E=",
            CertificateAuthorityData: "LS0tLS1CRUdJTiBDQQ==");

        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("client-certificate-data: LS0tLS1CRUdJTiBDRVJU", yaml);
        Assert.Contains("client-key-data: LS0tLS1CRUdJTiBSU0E=", yaml);
        Assert.Contains("certificate-authority-data: LS0tLS1CRUdJTiBDQQ==", yaml);
    }

    [Fact]
    public void CertAuth_WithNamespace_EmitsNamespaceInContext()
    {
        var entry = new CertificateKubeconfigEntry(
            ContextName: "cert-ns",
            ServerUrl: "https://k8s.example.com:6443",
            ClientCertificatePath: "/path/cert",
            ClientKeyPath: "/path/key",
            Namespace: "production");

        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("namespace: production", yaml);
    }

    // ── Exec auth ───────────────────────────────────────────────────────────

    [Fact]
    public void ExecAuth_EmitsApiVersionAndCommand()
    {
        var entry = new ExecKubeconfigEntry(
            ContextName: "exec-prod",
            ServerUrl: "https://k8s.example.com:6443",
            ExecCommand: "aws-iam-authenticator");

        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("apiVersion: client.authentication.k8s.io/v1beta1", yaml);
        Assert.Contains("command: aws-iam-authenticator", yaml);
    }

    [Fact]
    public void ExecAuth_EmitsArgsAsList()
    {
        var entry = new ExecKubeconfigEntry(
            ContextName: "exec-args",
            ServerUrl: "https://k8s.example.com:6443",
            ExecCommand: "aws-iam-authenticator",
            ExecArgs: ["token", "-i", "my-cluster"]);

        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("args:", yaml);
        Assert.Contains("- token", yaml);
        Assert.Contains("- -i", yaml);
        Assert.Contains("- my-cluster", yaml);
    }

    [Fact]
    public void ExecAuth_EmitsEnvAsNameValueList()
    {
        var entry = new ExecKubeconfigEntry(
            ContextName: "exec-env",
            ServerUrl: "https://k8s.example.com:6443",
            ExecCommand: "kubelogin",
            ExecEnv: new Dictionary<string, string>
            {
                ["AWS_PROFILE"] = "prod",
                ["REGION"] = "us-east-1",
            });

        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("env:", yaml);
        Assert.Contains("name: AWS_PROFILE", yaml);
        Assert.Contains("value: prod", yaml);
        Assert.Contains("name: REGION", yaml);
        Assert.Contains("value: us-east-1", yaml);
    }

    [Fact]
    public void ExecAuth_WithInstallHint_EmitsInstallHint()
    {
        var entry = new ExecKubeconfigEntry(
            ContextName: "exec-hint",
            ServerUrl: "https://k8s.example.com:6443",
            ExecCommand: "kubelogin",
            InstallHint: "brew install kubelogin");

        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("installHint: brew install kubelogin", yaml);
    }

    [Fact]
    public void ExecAuth_OutputIsParseable()
    {
        var entry = new ExecKubeconfigEntry(
            ContextName: "exec-parse",
            ServerUrl: "https://k8s.example.com:6443",
            ExecCommand: "aws-iam-authenticator",
            ExecArgs: ["token", "-i", "cluster"],
            ExecEnv: new Dictionary<string, string> { ["AWS_PROFILE"] = "default" },
            Namespace: "kube-system");

        _sut.WriteClusterEntry(entry);

        // Verify the output is valid YAML that round-trips.
        var yaml = File.ReadAllText(_kubeconfigPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        var doc = deserializer.Deserialize<Dictionary<object, object>>(yaml);
        Assert.NotNull(doc);
        Assert.Equal("exec-parse", doc["current-context"]);
    }

    [Fact]
    public void ExecAuth_FormattingIsDeterministic()
    {
        var entry = new ExecKubeconfigEntry(
            ContextName: "exec-determ",
            ServerUrl: "https://k8s.example.com:6443",
            ExecCommand: "gke-gcloud-auth-plugin",
            ExecArgs: ["--cluster", "prod"],
            ExecEnv: new Dictionary<string, string> { ["USE_GKE_GCLOUD_AUTH_PLUGIN"] = "True" });

        _sut.WriteClusterEntry(entry);
        var output1 = File.ReadAllText(_kubeconfigPath);

        File.Delete(_kubeconfigPath);
        var sut2 = new KubeconfigWriter(_kubeconfigPath);
        sut2.WriteClusterEntry(entry);
        var output2 = File.ReadAllText(_kubeconfigPath);

        Assert.Equal(output1, output2);
    }
}
