using System.Text.Json;
using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public sealed class ConfigCommandTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly string _tokenPath;
    private readonly string _kubeconfigPath;

    public ConfigCommandTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"clustral-cli-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _tokenPath = Path.Combine(_tempDir, "token");
        _kubeconfigPath = Path.Combine(_tempDir, "kube", "config");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    private string Render(ConfigShowOutput data)
    {
        var console = new TestConsole();
        console.Profile.Width = 100;
        ConfigCommand.Render(console, data);
        var output = console.Output;
        _output.WriteLine(output);
        return output;
    }

    // ── Collect (data) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Collect_NoFiles_ReturnsNotLoggedIn()
    {
        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);

        data.Files.Config.Exists.Should().BeFalse();
        data.Files.Token.Exists.Should().BeFalse();
        data.Files.Kubeconfig.Exists.Should().BeFalse();
        data.Session.Status.Should().Be("NotLoggedIn");
        data.ControlPlane.OidcClientId.Should().Be("clustral-cli"); // default
        data.Cli.Version.Should().StartWith("v");
    }

    [Fact]
    public async Task Collect_WithConfig_LoadsControlPlaneSettings()
    {
        var config = new CliConfig
        {
            ControlPlaneUrl = "https://cp.example.com",
            OidcAuthority = "https://kc.example.com/realms/clustral",
            OidcClientId = "my-client",
            OidcScopes = "openid email",
            CallbackPort = 8080,
            InsecureTls = true,
        };
        var json = JsonSerializer.Serialize(config, CliJsonContext.Default.CliConfig);
        await File.WriteAllTextAsync(_configPath, json);

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);

        data.Files.Config.Exists.Should().BeTrue();
        data.Files.Config.SizeBytes.Should().BeGreaterThan(0);
        data.ControlPlane.Url.Should().Be("https://cp.example.com");
        data.ControlPlane.OidcAuthority.Should().Be("https://kc.example.com/realms/clustral");
        data.ControlPlane.OidcClientId.Should().Be("my-client");
        data.ControlPlane.OidcScopes.Should().Be("openid email");
        data.ControlPlane.CallbackPort.Should().Be(8080);
        data.ControlPlane.InsecureTls.Should().BeTrue();
    }

    [Fact]
    public async Task Collect_WithValidToken_DecodesSession()
    {
        var jwt = BuildJwt("alice@example.com",
            issuedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            expiresAt: DateTimeOffset.UtcNow.AddHours(2));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);

        data.Files.Token.Exists.Should().BeTrue();
        data.Session.Status.Should().Be("LoggedIn");
        data.Session.Subject.Should().Be("alice@example.com");
        data.Session.IssuedAt.Should().NotBeNull();
        data.Session.ExpiresAt.Should().NotBeNull();
        data.Session.ValidForSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Collect_WithExpiredToken_StatusExpired()
    {
        var jwt = BuildJwt("bob@example.com",
            issuedAt: DateTimeOffset.UtcNow.AddHours(-2),
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);

        data.Session.Status.Should().Be("Expired");
        data.Session.Subject.Should().Be("bob@example.com");
        data.Session.ValidForSeconds.Should().Be(0);
    }

    [Fact]
    public async Task Collect_WithKubeconfig_ListsClustralContexts()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_kubeconfigPath)!);
        await File.WriteAllTextAsync(_kubeconfigPath, """
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
                token: t1
            - name: clustral-staging
              user:
                token: t2
            - name: minikube
              user:
                token: t3
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

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);

        data.Files.Kubeconfig.Exists.Should().BeTrue();
        data.Files.Kubeconfig.TotalContexts.Should().Be(3);
        data.Files.Kubeconfig.CurrentContext.Should().Be("clustral-prod");
        data.Files.Kubeconfig.ClustralContexts.Should().BeEquivalentTo(
            new[] { "clustral-prod", "clustral-staging" });
    }

    // ── Render ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Render_NoSession_ShowsNotLoggedInHint()
    {
        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);
        var rendered = Render(data);

        rendered.Should().Contain("Not logged in");
        rendered.Should().Contain("clustral login");
        rendered.Should().Contain("Control plane");
        rendered.Should().Contain("Session");
        rendered.Should().Contain("Profile");
        rendered.Should().Contain("Files");
        rendered.Should().Contain("CLI");
    }

    [Fact]
    public async Task Render_LoggedIn_ShowsSubjectAndExpiry()
    {
        var jwt = BuildJwt("alice@example.com",
            issuedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            expiresAt: DateTimeOffset.UtcNow.AddHours(4));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);
        var rendered = Render(data);

        rendered.Should().Contain("Logged in");
        rendered.Should().Contain("alice@example.com");
        rendered.Should().Contain("valid for");
    }

    [Fact]
    public async Task Render_Expired_ShowsExpiredAndHint()
    {
        var jwt = BuildJwt("bob@example.com",
            issuedAt: DateTimeOffset.UtcNow.AddHours(-2),
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);
        var rendered = Render(data);

        rendered.Should().Contain("expired");
        rendered.Should().Contain("clustral login");
        rendered.Should().Contain("bob@example.com");
    }

    // ── Edge cases: missing files / directories ─────────────────────────────

    [Fact]
    public async Task Collect_ParentDirectoryMissing_AllFilesMarkedNotExisting()
    {
        // Point at paths that include a non-existent parent directory.
        var missingDir = Path.Combine(_tempDir, "does", "not", "exist");
        var configPath = Path.Combine(missingDir, "config.json");
        var tokenPath = Path.Combine(missingDir, "token");
        var kubeconfigPath = Path.Combine(missingDir, "kube", "config");

        Directory.Exists(missingDir).Should().BeFalse();

        var data = await ConfigCommand.CollectAsync(configPath, tokenPath, kubeconfigPath);

        data.Files.Config.Exists.Should().BeFalse();
        data.Files.Config.SizeBytes.Should().Be(0);
        data.Files.Token.Exists.Should().BeFalse();
        data.Files.Token.SizeBytes.Should().Be(0);
        data.Files.Kubeconfig.Exists.Should().BeFalse();
        data.Files.Kubeconfig.TotalContexts.Should().Be(0);
        data.Files.Kubeconfig.CurrentContext.Should().BeNull();
        data.Session.Status.Should().Be("NotLoggedIn");

        // Render should not throw and should be informative.
        var rendered = Render(data);
        rendered.Should().Contain("Not logged in");
        rendered.Should().Contain("(does not exist)");
    }

    [Fact]
    public async Task Collect_OnlyTokenExists_ConfigShowsMissing()
    {
        var jwt = BuildJwt("alice@example.com",
            issuedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);

        data.Files.Config.Exists.Should().BeFalse();
        data.Files.Token.Exists.Should().BeTrue();
        data.Files.Kubeconfig.Exists.Should().BeFalse();
        data.Session.Status.Should().Be("LoggedIn");
        data.Session.Subject.Should().Be("alice@example.com");
        // ControlPlane settings come from defaults since config.json is absent.
        data.ControlPlane.Url.Should().BeEmpty();
        data.ControlPlane.OidcClientId.Should().Be("clustral-cli");
    }

    [Fact]
    public async Task Collect_OnlyConfigExists_SessionNotLoggedIn()
    {
        var config = new CliConfig { ControlPlaneUrl = "https://cp.example.com" };
        await File.WriteAllTextAsync(_configPath,
            JsonSerializer.Serialize(config, CliJsonContext.Default.CliConfig));

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);

        data.Files.Config.Exists.Should().BeTrue();
        data.Files.Token.Exists.Should().BeFalse();
        data.Files.Kubeconfig.Exists.Should().BeFalse();
        data.ControlPlane.Url.Should().Be("https://cp.example.com");
        data.Session.Status.Should().Be("NotLoggedIn");
        data.Session.Subject.Should().BeNull();
    }

    [Fact]
    public async Task Collect_KubeconfigMissing_ReportsZeroContexts()
    {
        // Other files present, kubeconfig missing.
        var jwt = BuildJwt("alice@example.com",
            issuedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        File.Exists(_kubeconfigPath).Should().BeFalse();

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);

        data.Files.Kubeconfig.Exists.Should().BeFalse();
        data.Files.Kubeconfig.TotalContexts.Should().Be(0);
        data.Files.Kubeconfig.CurrentContext.Should().BeNull();
        data.Files.Kubeconfig.ClustralContexts.Should().BeEmpty();

        var rendered = Render(data);
        rendered.Should().Contain("(does not exist)");
    }

    [Fact]
    public async Task Collect_KubeconfigParentDirMissing_HandledGracefully()
    {
        // Token present, kubeconfig path points into a non-existent dir.
        var jwt = BuildJwt("alice@example.com",
            issuedAt: DateTimeOffset.UtcNow,
            expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var deepKubeconfig = Path.Combine(_tempDir, "no", "such", "dir", "config");
        Directory.Exists(Path.GetDirectoryName(deepKubeconfig)!).Should().BeFalse();

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, deepKubeconfig);

        data.Files.Kubeconfig.Exists.Should().BeFalse();
        data.Files.Kubeconfig.TotalContexts.Should().Be(0);
        data.Session.Status.Should().Be("LoggedIn");
    }

    [Fact]
    public async Task Render_AllFilesMissing_DoesNotThrow()
    {
        var missingDir = Path.Combine(_tempDir, "ghost");
        var data = await ConfigCommand.CollectAsync(
            Path.Combine(missingDir, "config.json"),
            Path.Combine(missingDir, "token"),
            Path.Combine(missingDir, "kube", "config"));

        var act = () => Render(data);
        act.Should().NotThrow();
    }

    // ── JSON output ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Json_ProducesValidJson_WithExpectedShape()
    {
        var config = new CliConfig
        {
            ControlPlaneUrl = "https://cp.example.com",
            OidcClientId = "test",
        };
        await File.WriteAllTextAsync(_configPath,
            JsonSerializer.Serialize(config, CliJsonContext.Default.CliConfig));

        var data = await ConfigCommand.CollectAsync(_configPath, _tokenPath, _kubeconfigPath);
        var json = JsonSerializer.Serialize(data, CliJsonContext.Default.ConfigShowOutput);

        _output.WriteLine(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("files").GetProperty("config").GetProperty("exists").GetBoolean().Should().BeTrue();
        root.GetProperty("files").GetProperty("token").GetProperty("exists").GetBoolean().Should().BeFalse();
        root.GetProperty("controlPlane").GetProperty("url").GetString().Should().Be("https://cp.example.com");
        root.GetProperty("controlPlane").GetProperty("oidcClientId").GetString().Should().Be("test");
        root.GetProperty("session").GetProperty("status").GetString().Should().Be("NotLoggedIn");
        root.GetProperty("cli").GetProperty("version").GetString().Should().StartWith("v");
    }

    // ── Helper: build a fake JWT (header.payload.signature) ────────────────

    private static string BuildJwt(string email, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var payloadJson = $$"""
            {"email":"{{email}}","iat":{{issuedAt.ToUnixTimeSeconds()}},"exp":{{expiresAt.ToUnixTimeSeconds()}}}
            """;
        var payload = Base64UrlEncode(payloadJson);
        return $"{header}.{payload}.fakesig";
    }

    private static string Base64UrlEncode(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
