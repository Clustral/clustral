using System.CommandLine;
using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Tests for <c>clustral config clean</c> — factory reset of CLI state.
/// Uses a temp HOME to isolate file system operations.
/// </summary>
[Collection(ConfigCleanTestCollection.Name)]
public sealed class ConfigCleanTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _tempHome = Path.Combine(
        Path.GetTempPath(), $"clustral-clean-test-{Guid.NewGuid():N}");
    private readonly string? _origHome = Environment.GetEnvironmentVariable("HOME");
    private readonly string? _origUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
    private readonly string? _origKubeconfig = Environment.GetEnvironmentVariable("KUBECONFIG");

    private string ClustralDir => Path.Combine(_tempHome, ".clustral");
    private string KubeconfigPath => Path.Combine(_tempHome, ".kube", "config");

    private void SetupHome()
    {
        Directory.CreateDirectory(ClustralDir);
        Directory.CreateDirectory(Path.GetDirectoryName(KubeconfigPath)!);
        Environment.SetEnvironmentVariable("HOME", _tempHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _tempHome);
        Environment.SetEnvironmentVariable("KUBECONFIG", KubeconfigPath);
    }

    private void SeedState()
    {
        // Config
        File.WriteAllText(Path.Combine(ClustralDir, "config.json"),
            """{"controlPlaneUrl":"https://example.com"}""");

        // Token
        File.WriteAllText(Path.Combine(ClustralDir, "token"), "fake-jwt");

        // Active profile
        File.WriteAllText(Path.Combine(ClustralDir, "active-profile"), "staging");

        // Profiles
        var profilesDir = Path.Combine(ClustralDir, "profiles");
        Directory.CreateDirectory(Path.Combine(profilesDir, "staging"));
        File.WriteAllText(Path.Combine(profilesDir, "staging", "config.json"), "{}");
        File.WriteAllText(Path.Combine(profilesDir, "staging", "token"), "staging-jwt");
        Directory.CreateDirectory(Path.Combine(profilesDir, "prod"));
        File.WriteAllText(Path.Combine(profilesDir, "prod", "config.json"), "{}");

        // Kubeconfig with clustral contexts
        File.WriteAllText(KubeconfigPath, """
            apiVersion: v1
            kind: Config
            current-context: clustral-prod
            clusters:
            - name: clustral-prod
              cluster:
                server: https://example.com/api/proxy/prod
            - name: minikube
              cluster:
                server: https://192.168.49.2:8443
            users:
            - name: clustral-prod
              user:
                token: prod-token
            - name: minikube
              user:
                token: mini-token
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
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _origHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _origUserProfile);
        Environment.SetEnvironmentVariable("KUBECONFIG", _origKubeconfig);
        try { Directory.Delete(_tempHome, recursive: true); } catch { }
    }

    [Fact]
    public async Task Clean_WithYes_DeletesAllState()
    {
        SetupHome();
        SeedState();

        var root = new RootCommand();
        root.AddCommand(ConfigCommand.Build());
        var exit = await root.InvokeAsync(["config", "clean", "--yes"]);

        output.WriteLine($"Exit code: {exit}");

        exit.Should().Be(0);
        File.Exists(Path.Combine(ClustralDir, "config.json")).Should().BeFalse();
        File.Exists(Path.Combine(ClustralDir, "token")).Should().BeFalse();
        File.Exists(Path.Combine(ClustralDir, "active-profile")).Should().BeFalse();
        Directory.Exists(Path.Combine(ClustralDir, "profiles")).Should().BeFalse();

        // Kubeconfig should still exist but without clustral contexts
        File.Exists(KubeconfigPath).Should().BeTrue();
        var kubeconfig = File.ReadAllText(KubeconfigPath);
        kubeconfig.Should().NotContain("clustral-prod");
        kubeconfig.Should().Contain("minikube");
    }

    [Fact]
    public async Task Clean_DryRun_DoesNotDeleteAnything()
    {
        SetupHome();
        SeedState();

        var root = new RootCommand();
        root.AddCommand(ConfigCommand.Build());
        var exit = await root.InvokeAsync(["config", "clean", "--dry-run"]);

        exit.Should().Be(0);

        // Everything should still exist
        File.Exists(Path.Combine(ClustralDir, "config.json")).Should().BeTrue();
        File.Exists(Path.Combine(ClustralDir, "token")).Should().BeTrue();
        File.Exists(Path.Combine(ClustralDir, "active-profile")).Should().BeTrue();
        Directory.Exists(Path.Combine(ClustralDir, "profiles", "staging")).Should().BeTrue();
    }

    [Fact]
    public async Task Clean_NoState_StillSucceeds()
    {
        SetupHome();
        // Don't seed any state — empty ~/.clustral/

        var root = new RootCommand();
        root.AddCommand(ConfigCommand.Build());
        var exit = await root.InvokeAsync(["config", "clean", "--yes"]);

        exit.Should().Be(0);
    }

    [Fact]
    public async Task Clean_PreservesNonClustralKubecontexts()
    {
        SetupHome();
        SeedState();

        var root = new RootCommand();
        root.AddCommand(ConfigCommand.Build());
        await root.InvokeAsync(["config", "clean", "--yes"]);

        var kubeconfig = File.ReadAllText(KubeconfigPath);
        kubeconfig.Should().Contain("minikube", "non-clustral contexts must survive");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConfigCleanTestCollection
{
    public const string Name = "ConfigClean (env-var redirected, no parallelisation)";
}
