using Clustral.Cli.Commands;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class LogoutCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ITestOutputHelper _output;

    public LogoutCommandTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"clustral-logout-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteKubeconfig(string yaml)
    {
        var path = Path.Combine(_tempDir, "config");
        File.WriteAllText(path, yaml);
        return path;
    }

    [Fact]
    public void FindClustralContexts_FindsMultipleContexts()
    {
        var path = WriteKubeconfig("""
            apiVersion: v1
            kind: Config
            clusters:
            - name: clustral-prod
              cluster:
                server: https://cp.example.com/api/proxy/prod
            - name: clustral-staging
              cluster:
                server: https://cp.example.com/api/proxy/staging
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
                client-certificate: /path/to/cert
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
            current-context: clustral-prod
            """);

        var results = LogoutCommand.FindClustralContexts(path);

        _output.WriteLine("=== Found Clustral Contexts ===");
        foreach (var (name, token) in results)
            _output.WriteLine($"  {name} => token: {token ?? "(none)"}");

        Assert.Equal(2, results.Count);

        var prod = results.First(r => r.ContextName == "clustral-prod");
        Assert.Equal("prod-token-abc", prod.Token);

        var staging = results.First(r => r.ContextName == "clustral-staging");
        Assert.Equal("staging-token-xyz", staging.Token);
    }

    [Fact]
    public void FindClustralContexts_IgnoresNonClustralContexts()
    {
        var path = WriteKubeconfig("""
            apiVersion: v1
            kind: Config
            contexts:
            - name: minikube
              context:
                cluster: minikube
                user: minikube
            - name: docker-desktop
              context:
                cluster: docker-desktop
                user: docker-desktop
            users:
            - name: minikube
              user:
                token: mini-token
            - name: docker-desktop
              user:
                token: docker-token
            clusters: []
            current-context: minikube
            """);

        var results = LogoutCommand.FindClustralContexts(path);

        _output.WriteLine("=== Non-Clustral Contexts ===");
        _output.WriteLine($"  Found {results.Count} clustral contexts (expected 0)");
        _output.WriteLine("  minikube and docker-desktop correctly ignored");

        Assert.Empty(results);
    }

    [Fact]
    public void FindClustralContexts_HandlesNoUserToken()
    {
        var path = WriteKubeconfig("""
            apiVersion: v1
            kind: Config
            contexts:
            - name: clustral-notokenuser
              context:
                cluster: clustral-notokenuser
                user: clustral-notokenuser
            users:
            - name: clustral-notokenuser
              user:
                exec:
                  command: some-plugin
            clusters: []
            current-context: clustral-notokenuser
            """);

        var results = LogoutCommand.FindClustralContexts(path);

        _output.WriteLine("=== Exec-Auth User (no token) ===");
        foreach (var (name, token) in results)
            _output.WriteLine($"  {name} => token: {token ?? "(null - exec auth)"}");

        Assert.Single(results);
        Assert.Null(results[0].Token);
    }

    [Fact]
    public void FindClustralContexts_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var path = Path.Combine(_tempDir, "nonexistent");
        var results = LogoutCommand.FindClustralContexts(path);

        _output.WriteLine($"File: {path}");
        _output.WriteLine($"Exists: false => {results.Count} contexts");

        Assert.Empty(results);
    }

    [Fact]
    public void FindClustralContexts_ReturnsEmpty_WhenYamlIsInvalid()
    {
        var path = WriteKubeconfig("this is not valid yaml: [[[");
        var results = LogoutCommand.FindClustralContexts(path);

        _output.WriteLine("Input: invalid YAML");
        _output.WriteLine($"Result: {results.Count} contexts (gracefully handled)");

        Assert.Empty(results);
    }

    [Fact]
    public void FindClustralContexts_ReturnsEmpty_WhenNoContextsKey()
    {
        var path = WriteKubeconfig("""
            apiVersion: v1
            kind: Config
            clusters: []
            users: []
            current-context: ""
            """);

        var results = LogoutCommand.FindClustralContexts(path);

        _output.WriteLine("Input: kubeconfig with no 'contexts' key");
        _output.WriteLine($"Result: {results.Count} contexts");

        Assert.Empty(results);
    }
}
