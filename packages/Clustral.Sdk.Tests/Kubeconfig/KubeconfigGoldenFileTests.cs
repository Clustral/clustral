using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Category 19: golden file / snapshot tests.
/// Compares actual writer output against checked-in golden files.
/// To update golden files, set environment variable UPDATE_GOLDEN=1 and run tests.
/// </summary>
public sealed class KubeconfigGoldenFileTests : IDisposable
{
    private readonly string _tempDir;
    private static readonly DateTimeOffset AnyExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string GoldenDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Kubeconfig", "GoldenFiles");

    public KubeconfigGoldenFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-golden-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Golden_MinimalTokenConfig()
    {
        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-prod", "https://cp.example.com/api/proxy/prod",
            "eyJhbGciOi.token.sig", AnyExpiry));

        AssertMatchesGoldenFile(path, "minimal.yaml");
    }

    [Fact]
    public void Golden_ExecAuthConfig()
    {
        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ExecKubeconfigEntry(
            ContextName: "eks-prod",
            ServerUrl: "https://eks.us-east-1.amazonaws.com",
            ExecCommand: "aws-iam-authenticator",
            ExecArgs: ["token", "-i", "prod-cluster"],
            ExecEnv: new Dictionary<string, string> { ["AWS_PROFILE"] = "production" },
            CertificateAuthorityData: "LS0tLS1CRUdJTiBDRVJUSUZJQ0FURS0tLS0t",
            Namespace: "default"));

        AssertMatchesGoldenFile(path, "exec-auth-entry.yaml");
    }

    [Fact]
    public void Golden_MultiClusterConfig()
    {
        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-dev", "https://cp.example.com/api/proxy/dev", "dev-token", AnyExpiry),
            setCurrentContext: false);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-prod", "https://cp.example.com/api/proxy/prod", "prod-token", AnyExpiry),
            setCurrentContext: true);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-uat", "https://cp.example.com/api/proxy/uat", "uat-token", AnyExpiry),
            setCurrentContext: false);

        AssertMatchesGoldenFile(path, "multi-cluster.yaml");
    }

    [Fact]
    public void Golden_OutputIsStableAcrossRuns()
    {
        // First run.
        var path1 = Path.Combine(_tempDir, "config1");
        var sut1 = new KubeconfigWriter(path1);
        sut1.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-prod", "https://cp.example.com/api/proxy/prod",
            "stable-token", AnyExpiry));
        var output1 = File.ReadAllText(path1);

        // Second run (same input).
        var path2 = Path.Combine(_tempDir, "config2");
        var sut2 = new KubeconfigWriter(path2);
        sut2.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-prod", "https://cp.example.com/api/proxy/prod",
            "stable-token", AnyExpiry));
        var output2 = File.ReadAllText(path2);

        Assert.Equal(output1, output2);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AssertMatchesGoldenFile(string actualPath, string goldenFileName)
    {
        var actual = File.ReadAllText(actualPath);
        var goldenPath = Path.Combine(GoldenDir, goldenFileName);

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, actual);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            // Auto-create golden file on first run.
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, actual);
            // Still pass the test — the file is now the baseline.
            return;
        }

        var expected = File.ReadAllText(goldenPath);
        Assert.Equal(expected, actual);
    }
}
