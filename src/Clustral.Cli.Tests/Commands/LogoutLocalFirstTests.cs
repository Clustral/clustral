using Clustral.Cli.Commands;
using Clustral.Sdk.Kubeconfig;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Verifies the local-first cleanup invariant of <c>clustral logout</c> and
/// <c>clustral kube logout</c>: even if the ControlPlane is unreachable, the
/// kubeconfig contexts and the credential token must still be cleaned up
/// locally.
///
/// Tests focus on the local-mutation primitives (FindClustralContexts +
/// KubeconfigWriter.RemoveClusterEntry) so they're hermetic and don't depend
/// on a running ControlPlane.
/// </summary>
public sealed class LogoutLocalFirstTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;

    public LogoutLocalFirstTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"clustral-logout-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _kubeconfigPath = Path.Combine(_tempDir, "kube", "config");
        Directory.CreateDirectory(Path.GetDirectoryName(_kubeconfigPath)!);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    private void WriteKubeconfig(string yaml) => File.WriteAllText(_kubeconfigPath, yaml);

    [Fact]
    public void FindClustralContexts_ExtractsTokens()
    {
        WriteKubeconfig("""
            apiVersion: v1
            kind: Config
            current-context: clustral-prod
            clusters:
            - name: clustral-prod
              cluster:
                server: https://prod.example.com
            - name: clustral-staging
              cluster:
                server: https://staging.example.com
            - name: minikube
              cluster:
                server: https://192.168.49.2:8443
            users:
            - name: clustral-prod
              user:
                token: prod-token-abc
            - name: clustral-staging
              user:
                token: staging-token-xyz
            - name: minikube
              user:
                token: should-not-be-included
            contexts:
            - name: clustral-prod
              context:
                cluster: clustral-prod
                user: clustral-prod
            - name: clustral-staging
              context:
                cluster: clustral-staging
                user: clustral-staging
            - name: minikube
              context:
                cluster: minikube
                user: minikube
            """);

        var contexts = LogoutCommand.FindClustralContexts(_kubeconfigPath);

        contexts.Should().HaveCount(2, "only clustral-* contexts should be returned");
        contexts.Should().Contain(c => c.ContextName == "clustral-prod" && c.Token == "prod-token-abc");
        contexts.Should().Contain(c => c.ContextName == "clustral-staging" && c.Token == "staging-token-xyz");
        contexts.Should().NotContain(c => c.ContextName == "minikube");
    }

    [Fact]
    public void FindClustralContexts_NoKubeconfig_ReturnsEmpty()
    {
        var missingPath = Path.Combine(_tempDir, "no-such-kubeconfig");

        var contexts = LogoutCommand.FindClustralContexts(missingPath);

        contexts.Should().BeEmpty();
    }

    [Fact]
    public void FindClustralContexts_NoClustralContexts_ReturnsEmpty()
    {
        WriteKubeconfig("""
            apiVersion: v1
            kind: Config
            clusters:
            - name: minikube
              cluster:
                server: https://192.168.49.2:8443
            users:
            - name: minikube
              user:
                token: t1
            contexts:
            - name: minikube
              context:
                cluster: minikube
                user: minikube
            """);

        LogoutCommand.FindClustralContexts(_kubeconfigPath).Should().BeEmpty();
    }

    [Fact]
    public void KubeconfigWriter_RemoveClusterEntry_PreservesOtherContexts()
    {
        // Local cleanup primitive — independent of any HTTP call.
        WriteKubeconfig("""
            apiVersion: v1
            kind: Config
            current-context: clustral-prod
            clusters:
            - name: clustral-prod
              cluster:
                server: https://prod.example.com
            - name: minikube
              cluster:
                server: https://192.168.49.2:8443
            users:
            - name: clustral-prod
              user:
                token: t1
            - name: minikube
              user:
                token: t2
            contexts:
            - name: clustral-prod
              context:
                cluster: clustral-prod
                user: clustral-prod
            - name: minikube
              context:
                cluster: minikube
                user: minikube
            """);

        var writer = new KubeconfigWriter(_kubeconfigPath);
        writer.RemoveClusterEntry("clustral-prod");

        var remaining = writer.ListContextNames();
        remaining.Should().NotContain("clustral-prod");
        remaining.Should().Contain("minikube");
        _output.WriteLine($"Remaining contexts: {string.Join(", ", remaining)}");
    }

    [Fact]
    public void KubeconfigWriter_RemoveAllClustralContexts_LeavesNonClustralAlone()
    {
        // Simulates `clustral logout` local cleanup phase: iterate the list of
        // clustral contexts and remove each. This must work even if the
        // ControlPlane is down (no HTTP calls involved).
        WriteKubeconfig("""
            apiVersion: v1
            kind: Config
            current-context: clustral-prod
            clusters:
            - name: clustral-prod
              cluster: { server: https://prod }
            - name: clustral-staging
              cluster: { server: https://staging }
            - name: minikube
              cluster: { server: https://192.168.49.2 }
            users:
            - name: clustral-prod
              user: { token: a }
            - name: clustral-staging
              user: { token: b }
            - name: minikube
              user: { token: c }
            contexts:
            - name: clustral-prod
              context: { cluster: clustral-prod, user: clustral-prod }
            - name: clustral-staging
              context: { cluster: clustral-staging, user: clustral-staging }
            - name: minikube
              context: { cluster: minikube, user: minikube }
            """);

        var contexts = LogoutCommand.FindClustralContexts(_kubeconfigPath);
        var writer = new KubeconfigWriter(_kubeconfigPath);

        foreach (var (contextName, _) in contexts)
        {
            writer.RemoveClusterEntry(contextName);
        }

        var remaining = writer.ListContextNames();
        remaining.Should().BeEquivalentTo(new[] { "minikube" });
    }
}
