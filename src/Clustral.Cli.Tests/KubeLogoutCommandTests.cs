using Clustral.Cli.Commands;
using Clustral.Sdk.Kubeconfig;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class KubeLogoutCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;
    private readonly ITestOutputHelper _output;

    public KubeLogoutCommandTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"kube-logout-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _kubeconfigPath = Path.Combine(_tempDir, "config");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static readonly DateTimeOffset AnyExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ── Command tree ────────────────────────────────────────────────────────

    [Fact]
    public void KubeCommand_HasLogoutSubcommand()
    {
        var kube = KubeLoginCommand.BuildKubeCommand();
        var subs = kube.Subcommands.Select(s => s.Name).ToList();

        _output.WriteLine($"kube subcommands: [{string.Join(", ", subs)}]");

        Assert.Contains("logout", subs);
    }

    [Fact]
    public void KubeLogout_HasClusterArgument()
    {
        var cmd = KubeLogoutCommand.Build();

        _output.WriteLine($"Command: {cmd.Name}");
        foreach (var arg in cmd.Arguments)
            _output.WriteLine($"  <{arg.Name}>");
        foreach (var opt in cmd.Options)
            _output.WriteLine($"  --{opt.Name}");

        Assert.Equal("logout", cmd.Name);
        Assert.Single(cmd.Arguments);
        Assert.Contains(cmd.Options, o => o.Name == "insecure");
    }

    // ── Context resolution ──────────────────────────────────────────────────

    [Fact]
    public void ContextName_PrefixedWithClustral()
    {
        // If user passes "prod", context name is "clustral-prod".
        var input = "prod";
        var contextName = input.StartsWith("clustral-", StringComparison.Ordinal)
            ? input
            : $"clustral-{input}";

        _output.WriteLine($"Input: \"{input}\" => contextName: \"{contextName}\"");

        Assert.Equal("clustral-prod", contextName);
    }

    [Fact]
    public void ContextName_AlreadyPrefixed_NotDoubled()
    {
        var input = "clustral-prod";
        var contextName = input.StartsWith("clustral-", StringComparison.Ordinal)
            ? input
            : $"clustral-{input}";

        _output.WriteLine($"Input: \"{input}\" => contextName: \"{contextName}\"");

        Assert.Equal("clustral-prod", contextName);
    }

    // ── Kubeconfig removal ──────────────────────────────────────────────────

    [Fact]
    public void RemovesSpecificContext_LeavesOthers()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-prod", "https://cp.example.com/proxy/prod", "prod-token", AnyExpiry));
        writer.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-staging", "https://cp.example.com/proxy/staging", "staging-token", AnyExpiry));

        _output.WriteLine("=== Before logout ===");
        _output.WriteLine(File.ReadAllText(_kubeconfigPath));

        // Simulate kube logout prod — remove only clustral-prod.
        writer.RemoveClusterEntry("clustral-prod");

        var yaml = File.ReadAllText(_kubeconfigPath);
        _output.WriteLine("=== After logout (prod removed) ===");
        _output.WriteLine(yaml);

        Assert.DoesNotContain("clustral-prod", yaml);
        Assert.Contains("clustral-staging", yaml);
        Assert.Contains("staging-token", yaml);
    }

    [Fact]
    public void RemovesContext_FallsBackCurrentContext()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-alpha", "https://cp.example.com/proxy/alpha", "alpha-tok", AnyExpiry));
        writer.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-beta", "https://cp.example.com/proxy/beta", "beta-tok", AnyExpiry));

        // current-context is now clustral-beta (last written).
        writer.RemoveClusterEntry("clustral-beta");

        var yaml = File.ReadAllText(_kubeconfigPath);
        _output.WriteLine("=== After removing active context ===");
        _output.WriteLine(yaml);

        Assert.Contains("current-context: clustral-alpha", yaml);
    }

    [Fact]
    public void RemovesLastContext_EmptyCurrentContext()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-only", "https://cp.example.com/proxy/only", "only-tok", AnyExpiry));

        writer.RemoveClusterEntry("clustral-only");

        var yaml = File.ReadAllText(_kubeconfigPath);
        _output.WriteLine("=== After removing last context ===");
        _output.WriteLine(yaml);

        Assert.DoesNotContain("clustral-only", yaml);
        Assert.Contains("current-context:", yaml);
    }

    [Fact]
    public void PreservesNonClustralEntries()
    {
        File.WriteAllText(_kubeconfigPath, """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: minikube
              cluster:
                server: https://192.168.49.2:8443
            - name: clustral-prod
              cluster:
                server: https://cp.example.com/proxy/prod
            users:
            - name: minikube
              user:
                client-certificate: /path/cert
            - name: clustral-prod
              user:
                token: prod-token
            contexts:
            - name: minikube
              context:
                cluster: minikube
                user: minikube
            - name: clustral-prod
              context:
                cluster: clustral-prod
                user: clustral-prod
            current-context: clustral-prod
            """);

        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.RemoveClusterEntry("clustral-prod");

        var yaml = File.ReadAllText(_kubeconfigPath);
        _output.WriteLine("=== After removing clustral-prod (minikube preserved) ===");
        _output.WriteLine(yaml);

        Assert.Contains("minikube", yaml);
        Assert.DoesNotContain("clustral-prod", yaml);
        Assert.DoesNotContain("prod-token", yaml);
    }

    // ── Token discovery for revocation ───────────────────────────────────────

    [Fact]
    public void FindClustralContexts_FindsTokenForRevocation()
    {
        File.WriteAllText(_kubeconfigPath, """
            apiVersion: v1
            kind: Config
            clusters:
            - name: clustral-prod
              cluster:
                server: https://cp.example.com/proxy/prod
            users:
            - name: clustral-prod
              user:
                token: revoke-me-token
            contexts:
            - name: clustral-prod
              context:
                cluster: clustral-prod
                user: clustral-prod
            current-context: clustral-prod
            """);

        var contexts = LogoutCommand.FindClustralContexts(_kubeconfigPath);
        var prod = contexts.FirstOrDefault(c => c.ContextName == "clustral-prod");

        _output.WriteLine($"Found context: {prod.ContextName}");
        _output.WriteLine($"Token:         {prod.Token}");

        Assert.Equal("clustral-prod", prod.ContextName);
        Assert.Equal("revoke-me-token", prod.Token);
    }

    [Fact]
    public void FindClustralContexts_NoToken_StillRemovesContext()
    {
        File.WriteAllText(_kubeconfigPath, """
            apiVersion: v1
            kind: Config
            clusters:
            - name: clustral-expired
              cluster:
                server: https://cp.example.com/proxy/expired
            users:
            - name: clustral-expired
              user:
                exec:
                  command: some-plugin
            contexts:
            - name: clustral-expired
              context:
                cluster: clustral-expired
                user: clustral-expired
            current-context: clustral-expired
            """);

        var contexts = LogoutCommand.FindClustralContexts(_kubeconfigPath);
        var ctx = contexts.First(c => c.ContextName == "clustral-expired");

        _output.WriteLine($"Context: {ctx.ContextName}, Token: {ctx.Token ?? "(null — exec auth, skip revocation)"}");

        Assert.Null(ctx.Token);

        // Still remove the context even without a token to revoke.
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.RemoveClusterEntry("clustral-expired");

        var yaml = File.ReadAllText(_kubeconfigPath);
        _output.WriteLine("=== After removal ===");
        _output.WriteLine(yaml);

        Assert.DoesNotContain("clustral-expired", yaml);
    }

    [Fact]
    public void NonExistentContext_NoError()
    {
        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-keep", "https://cp.example.com/proxy/keep", "keep-tok", AnyExpiry));

        // Removing a non-existent context should not throw or affect existing entries.
        writer.RemoveClusterEntry("clustral-nonexistent");

        var yaml = File.ReadAllText(_kubeconfigPath);
        _output.WriteLine("=== After removing non-existent context ===");
        _output.WriteLine(yaml);

        Assert.Contains("clustral-keep", yaml);
    }
}
