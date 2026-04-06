using Clustral.Sdk.Kubeconfig;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class KubeconfigWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;
    private readonly ITestOutputHelper _output;

    public KubeconfigWriterTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"clustral-kube-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _kubeconfigPath = Path.Combine(_tempDir, "config");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ClustralKubeconfigEntry MakeEntry(
        string name = "clustral-test",
        string server = "https://cp.example.com/api/proxy/test",
        string token = "test-token") =>
        new(name, server, token, DateTimeOffset.UtcNow.AddHours(8));

    private void DumpYaml(string label)
    {
        var yaml = File.ReadAllText(_kubeconfigPath);
        _output.WriteLine($"=== {label} ===");
        _output.WriteLine(yaml);
    }

    [Fact]
    public void WriteClusterEntry_CreatesNewFile()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(MakeEntry());

        DumpYaml("New kubeconfig");

        Assert.True(File.Exists(_kubeconfigPath));
        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("clustral-test", yaml);
        Assert.Contains("test-token", yaml);
        Assert.Contains("current-context: clustral-test", yaml);
    }

    [Fact]
    public void WriteClusterEntry_UpsertsSameContext()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(MakeEntry(token: "token-1"));
        writer.WriteClusterEntry(MakeEntry(token: "token-2"));

        DumpYaml("After upsert (token-1 -> token-2)");

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("token-2", yaml);
        Assert.DoesNotContain("token-1", yaml);
        Assert.Equal(3, CountOccurrences(yaml, "name: clustral-test"));
    }

    [Fact]
    public void WriteClusterEntry_MultipleContexts()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(MakeEntry("clustral-prod", token: "prod-token"));
        writer.WriteClusterEntry(MakeEntry("clustral-staging", token: "staging-token"));

        DumpYaml("Multiple contexts (prod + staging)");

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("clustral-prod", yaml);
        Assert.Contains("clustral-staging", yaml);
        Assert.Contains("current-context: clustral-staging", yaml);
    }

    [Fact]
    public void WriteClusterEntry_NoSetContext()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(MakeEntry("clustral-first"), setCurrentContext: true);
        writer.WriteClusterEntry(MakeEntry("clustral-second"), setCurrentContext: false);

        DumpYaml("No set context (first=true, second=false)");

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("current-context: clustral-first", yaml);
    }

    [Fact]
    public void WriteClusterEntry_InsecureSkipTlsVerify()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        var entry = new ClustralKubeconfigEntry(
            "clustral-dev", "https://localhost/proxy/dev", "dev-token",
            DateTimeOffset.UtcNow.AddHours(1), InsecureSkipTlsVerify: true);
        writer.WriteClusterEntry(entry);

        DumpYaml("Insecure skip TLS");

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("insecure-skip-tls-verify: true", yaml);
    }

    [Fact]
    public void WriteClusterEntry_PreservesExistingEntries()
    {
        File.WriteAllText(_kubeconfigPath, """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: minikube
              cluster:
                server: https://192.168.49.2:8443
                certificate-authority: /home/user/.minikube/ca.crt
            users:
            - name: minikube
              user:
                client-certificate: /home/user/.minikube/profiles/minikube/client.crt
            contexts:
            - name: minikube
              context:
                cluster: minikube
                user: minikube
            current-context: minikube
            """);

        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(MakeEntry("clustral-prod"));

        DumpYaml("Preserved minikube + added clustral-prod");

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("minikube", yaml);
        Assert.Contains("clustral-prod", yaml);
        Assert.Contains("current-context: clustral-prod", yaml);
    }

    [Fact]
    public void RemoveClusterEntry_RemovesAllThreeEntries()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(MakeEntry("clustral-remove-me"));

        _output.WriteLine("=== Before remove ===");
        _output.WriteLine(File.ReadAllText(_kubeconfigPath));

        writer.RemoveClusterEntry("clustral-remove-me");

        DumpYaml("After remove");

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.DoesNotContain("clustral-remove-me", yaml);
    }

    [Fact]
    public void RemoveClusterEntry_FallsBackCurrentContext()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(MakeEntry("clustral-alpha"));
        writer.WriteClusterEntry(MakeEntry("clustral-beta"));

        writer.RemoveClusterEntry("clustral-beta");

        DumpYaml("After removing beta (fallback to alpha)");

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("current-context: clustral-alpha", yaml);
        Assert.DoesNotContain("clustral-beta", yaml);
    }

    [Fact]
    public void RemoveClusterEntry_NoOpForNonExistent()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(MakeEntry("clustral-keep"));

        writer.RemoveClusterEntry("clustral-nonexistent");

        DumpYaml("After removing non-existent (no-op)");

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("clustral-keep", yaml);
    }

    [Fact]
    public void RemoveClusterEntry_EmptyCurrentContextWhenLastRemoved()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(MakeEntry("clustral-only"));
        writer.RemoveClusterEntry("clustral-only");

        DumpYaml("After removing last entry");

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("current-context: ''", yaml);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
