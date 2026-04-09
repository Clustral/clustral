using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Clustral.Cli.Commands;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Integration test that runs the real <c>clustral logout</c> command end-to-end
/// against an in-process <see cref="HttpListener"/> on a free TCP port.
///
/// This is the answer to the question "are we disposing the JWT earlier than
/// we could have the context tokens revocated?" — the recorded
/// <c>Authorization</c> headers prove the JWT is still being sent on every
/// <c>/api/v1/auth/revoke-by-token</c> POST, even though
/// <c>~/.clustral/token</c> has already been deleted by the local-cleanup
/// phase. The fix invariant is: read JWT into a local variable BEFORE
/// <c>cache.ClearAsync</c>, so the in-memory string outlives the file.
///
/// Uses <c>HOME</c> / <c>KUBECONFIG</c> environment variable redirection so
/// the CLI's normal <c>CliConfig.Load()</c> + <c>KubeconfigWriter</c> code
/// paths run unchanged. The collection definition disables parallelisation
/// across the entire test assembly so the process-wide env var redirection
/// cannot race with parallel test classes that also read those variables.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LogoutCommandIntegrationCollection
{
    public const string Name = "LogoutCommandIntegration (env-var redirected, no parallelisation)";
}

[Collection(LogoutCommandIntegrationCollection.Name)]
public sealed class LogoutCommandIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    // Fake ControlPlane.
    private readonly HttpListener _listener;
    private readonly string _baseUrl;
    private readonly CancellationTokenSource _serverCts = new();
    private readonly Task _serverLoop;
    private readonly List<RecordedRequest> _requests = new();
    private readonly object _requestsLock = new();
    private int _responseStatusCode = 200;

    // Temp HOME.
    private readonly string _tempHome;
    private readonly string _kubeconfigPath;
    private readonly string? _origHome;
    private readonly string? _origUserProfile;
    private readonly string? _origKubeconfig;

    public LogoutCommandIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        // ── 1. Start a fake ControlPlane on a free port ─────────────────
        var port = GetFreePort();
        _baseUrl = $"http://127.0.0.1:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{_baseUrl}/");
        _listener.Start();
        _serverLoop = Task.Run(() => HandleRequestsAsync(_serverCts.Token));

        // ── 2. Redirect HOME so the CLI reads OUR config + token ────────
        _tempHome = Path.Combine(Path.GetTempPath(), $"clustral-logout-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempHome, ".clustral"));
        _kubeconfigPath = Path.Combine(_tempHome, ".kube", "config");
        Directory.CreateDirectory(Path.GetDirectoryName(_kubeconfigPath)!);

        _origHome        = Environment.GetEnvironmentVariable("HOME");
        _origUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        _origKubeconfig  = Environment.GetEnvironmentVariable("KUBECONFIG");

        Environment.SetEnvironmentVariable("HOME", _tempHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _tempHome);
        Environment.SetEnvironmentVariable("KUBECONFIG", _kubeconfigPath);
    }

    public void Dispose()
    {
        try { _serverCts.Cancel(); } catch { }
        try { _listener.Stop(); _listener.Close(); } catch { }
        try { _serverLoop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _serverCts.Dispose();

        Environment.SetEnvironmentVariable("HOME", _origHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _origUserProfile);
        Environment.SetEnvironmentVariable("KUBECONFIG", _origKubeconfig);

        try { Directory.Delete(_tempHome, recursive: true); }
        catch { /* best effort */ }
    }

    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesEachKubeconfigCredential_WithJwtInAuthorizationHeader()
    {
        const string jwt        = "fake-session-jwt-abc";
        const string credToken1 = "credential-token-cluster-a";
        const string credToken2 = "credential-token-cluster-b";

        WriteToken(jwt);
        WriteConfig(_baseUrl);
        WriteKubeconfig(
            ("clustral-cluster-a", credToken1),
            ("clustral-cluster-b", credToken2));

        // Run the actual `clustral logout` command — same code path as production.
        var root = new RootCommand();
        root.AddCommand(LogoutCommand.Build());
        var exit = await root.InvokeAsync(["logout"]);

        exit.Should().Be(0);

        // ── Local cleanup happened ──────────────────────────────────────
        File.Exists(Path.Combine(_tempHome, ".clustral", "token"))
            .Should().BeFalse("the JWT cache must be cleared after logout");

        var remainingKubeconfig = File.ReadAllText(_kubeconfigPath);
        remainingKubeconfig.Should().NotContain("clustral-cluster-a");
        remainingKubeconfig.Should().NotContain("clustral-cluster-b");

        // ── Remote revocation happened — even after the cache was cleared ─
        var snapshot = SnapshotRequests();
        foreach (var r in snapshot)
            _output.WriteLine($"  → {r.Method} {r.Path}  Auth=[{r.AuthHeader}]  body={r.Body}");

        var revokes = snapshot
            .Where(r => r.Path == "/api/v1/auth/revoke-by-token")
            .ToList();

        revokes.Should().HaveCount(2,
            "logout must POST one revoke-by-token per clustral-* kubeconfig context");

        revokes.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be("POST");
            r.AuthHeader.Should().Be($"Bearer {jwt}",
                "the in-memory JWT must still be sent even though the cache file " +
                "has already been deleted by the local-first cleanup phase");
        });

        var bodies = revokes.Select(r => r.Body).ToList();
        bodies.Should().Contain(b => b.Contains(credToken1));
        bodies.Should().Contain(b => b.Contains(credToken2));
    }

    [Fact]
    public async Task Logout_NoClustralContexts_DoesNotPostAnything()
    {
        WriteToken("fake-jwt");
        WriteConfig(_baseUrl);
        File.WriteAllText(_kubeconfigPath, """
            apiVersion: v1
            kind: Config
            clusters:
            - name: minikube
              cluster:
                server: https://192.168.49.2:8443
            users:
            - name: minikube
              user:
                token: minikube-token
            contexts:
            - name: minikube
              context:
                cluster: minikube
                user: minikube
            current-context: minikube
            """);

        var root = new RootCommand();
        root.AddCommand(LogoutCommand.Build());
        var exit = await root.InvokeAsync(["logout"]);

        exit.Should().Be(0);
        File.Exists(Path.Combine(_tempHome, ".clustral", "token")).Should().BeFalse();
        SnapshotRequests().Should().BeEmpty(
            "no clustral-* contexts means there is nothing to revoke");
    }

    [Fact]
    public async Task Logout_ServerReturnsError_LocalCleanupStillCompletes_ExitsZero()
    {
        // Even if the server rejects the revoke, logout must still:
        //   - delete the local kubeconfig contexts
        //   - clear the token cache
        //   - exit 0 (logout is local-first, remote is best-effort)
        _responseStatusCode = 500;

        const string jwt = "fake-jwt";
        WriteToken(jwt);
        WriteConfig(_baseUrl);
        WriteKubeconfig(("clustral-orphan", "stale-token"));

        var root = new RootCommand();
        root.AddCommand(LogoutCommand.Build());
        var exit = await root.InvokeAsync(["logout"]);

        exit.Should().Be(0);
        File.Exists(Path.Combine(_tempHome, ".clustral", "token")).Should().BeFalse();
        File.ReadAllText(_kubeconfigPath).Should().NotContain("clustral-orphan");

        var snapshot = SnapshotRequests();
        snapshot.Should().HaveCount(1,
            "the POST must still be attempted even when the server returns an error");
        snapshot[0].AuthHeader.Should().Be($"Bearer {jwt}",
            "the JWT must be in the auth header even on the failure path");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void WriteToken(string jwt) =>
        File.WriteAllText(Path.Combine(_tempHome, ".clustral", "token"), jwt);

    private void WriteConfig(string controlPlaneUrl) =>
        File.WriteAllText(Path.Combine(_tempHome, ".clustral", "config.json"),
            $$"""
            {
              "controlPlaneUrl": "{{controlPlaneUrl}}",
              "oidcAuthority": "http://localhost:8080/realms/clustral",
              "oidcClientId": "clustral-cli",
              "callbackPort": 7777,
              "insecureTls": false
            }
            """);

    private void WriteKubeconfig(params (string contextName, string token)[] entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: v1");
        sb.AppendLine("kind: Config");
        sb.AppendLine($"current-context: {entries[0].contextName}");
        sb.AppendLine("clusters:");
        foreach (var (name, _) in entries)
        {
            sb.AppendLine($"- name: {name}");
            sb.AppendLine("  cluster:");
            sb.AppendLine($"    server: https://example.test/api/proxy/{name}");
        }
        sb.AppendLine("users:");
        foreach (var (name, token) in entries)
        {
            sb.AppendLine($"- name: {name}");
            sb.AppendLine("  user:");
            sb.AppendLine($"    token: {token}");
        }
        sb.AppendLine("contexts:");
        foreach (var (name, _) in entries)
        {
            sb.AppendLine($"- name: {name}");
            sb.AppendLine("  context:");
            sb.AppendLine($"    cluster: {name}");
            sb.AppendLine($"    user: {name}");
        }
        File.WriteAllText(_kubeconfigPath, sb.ToString());
    }

    private List<RecordedRequest> SnapshotRequests()
    {
        lock (_requestsLock) return _requests.ToList();
    }

    private async Task HandleRequestsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (HttpListenerException)   { return; }
            catch (ObjectDisposedException) { return; }
            catch (InvalidOperationException) { return; }

            try
            {
                var auth = ctx.Request.Headers["Authorization"];
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync(ct);

                lock (_requestsLock)
                {
                    _requests.Add(new RecordedRequest(
                        Method:     ctx.Request.HttpMethod,
                        Path:       ctx.Request.Url!.AbsolutePath,
                        AuthHeader: auth,
                        Body:       body));
                }

                ctx.Response.StatusCode  = _responseStatusCode;
                ctx.Response.ContentType = "application/json";
                var responseBytes = Encoding.UTF8.GetBytes(
                    """{"revoked":true,"revokedAt":"2026-04-09T00:00:00Z"}""");
                await ctx.Response.OutputStream.WriteAsync(responseBytes, ct);
                ctx.Response.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private sealed record RecordedRequest(
        string Method, string Path, string? AuthHeader, string Body);
}
