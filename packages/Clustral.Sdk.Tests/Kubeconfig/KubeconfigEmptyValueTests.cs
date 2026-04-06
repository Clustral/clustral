using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Category 8: empty and optional value handling.
/// </summary>
public sealed class KubeconfigEmptyValueTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;
    private readonly KubeconfigWriter _sut;

    private static readonly DateTimeOffset AnyExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public KubeconfigEmptyValueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-empty-{Guid.NewGuid():N}");
        _kubeconfigPath = Path.Combine(_tempDir, "config");
        _sut = new KubeconfigWriter(_kubeconfigPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void EmptyPreferences_RendersConsistently()
    {
        _sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-test", "https://test.example.com", "tok", AnyExpiry));

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("preferences:", yaml);
    }

    [Fact]
    public void OptionalInsecureSkipTls_OmittedWhenFalse()
    {
        _sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-secure", "https://secure.example.com", "tok", AnyExpiry,
            InsecureSkipTlsVerify: false));

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.DoesNotContain("insecure-skip-tls-verify", yaml);
    }

    [Fact]
    public void OptionalInsecureSkipTls_EmittedWhenTrue()
    {
        _sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-dev", "https://dev.example.com", "tok", AnyExpiry,
            InsecureSkipTlsVerify: true));

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("insecure-skip-tls-verify: true", yaml);
    }

    [Fact]
    public void CertEntry_OmitsNullOptionalFields()
    {
        var entry = new CertificateKubeconfigEntry(
            ContextName: "cert-minimal",
            ServerUrl: "https://k8s.example.com",
            ClientCertificatePath: "/path/cert",
            ClientKeyPath: "/path/key");

        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        // These optional fields should not appear.
        Assert.DoesNotContain("certificate-authority:", yaml);
        Assert.DoesNotContain("certificate-authority-data:", yaml);
        Assert.DoesNotContain("namespace:", yaml);
        Assert.DoesNotContain("insecure-skip-tls-verify:", yaml);
    }

    [Fact]
    public void ExecEntry_OmitsEmptyArgsAndEnv()
    {
        var entry = new ExecKubeconfigEntry(
            ContextName: "exec-minimal",
            ServerUrl: "https://k8s.example.com",
            ExecCommand: "kubelogin");

        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("command: kubelogin", yaml);
        Assert.DoesNotContain("args:", yaml);
        Assert.DoesNotContain("env:", yaml);
        Assert.DoesNotContain("installHint:", yaml);
    }

    [Fact]
    public void EmptyCurrentContext_EmittedAsEmptyString()
    {
        _sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-test", "https://test.example.com", "tok", AnyExpiry),
            setCurrentContext: false);

        var yaml = File.ReadAllText(_kubeconfigPath);
        // current-context should be empty string, not omitted.
        Assert.Contains("current-context:", yaml);
    }
}
