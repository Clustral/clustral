using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Testcontainers.K3s;
using Testcontainers.MongoDb;

namespace Clustral.E2E.Tests.Fixtures;

/// <summary>
/// Orchestrates the full Clustral stack for end-to-end tests:
/// MongoDB, Keycloak, K3s, ControlPlane, plus per-test Agent containers.
/// All built from production Dockerfiles, all on a shared Docker network —
/// the same code path that runs in production.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    // ─── Network alias constants (must match container hostnames) ─────────
    public const string KeycloakAlias = "keycloak";
    public const string MongoAlias = "mongo";
    public const string K3sAlias = "k3s";
    public const string ControlPlaneAlias = "controlplane";
    public const string KeycloakRealm = "clustral";

    // Internal ports as exposed by each service.
    private const int KeycloakInternalPort = 8080;
    private const int ControlPlaneRestPort = 5100;
    private const int ControlPlaneGrpcPort = 5443;
    private const int K3sInternalPort = 6443;
    private const int MongoInternalPort = 27017;

    private INetwork _network = null!;
    private MongoDbContainer _mongo = null!;
    private IContainer _keycloak = null!;
    private K3sContainer _k3s = null!;
    private IContainer _controlPlane = null!;
    private IFutureDockerImage _controlPlaneImage = null!;
    private IFutureDockerImage _agentImage = null!;

    private string _tempCaDir = null!;
    private string _caCertPath = null!;
    private string _caKeyPath = null!;

    public string K3sKubeconfig { get; private set; } = string.Empty;

    public Uri ControlPlaneRestUrl =>
        new($"http://{_controlPlane.Hostname}:{_controlPlane.GetMappedPublicPort(ControlPlaneRestPort)}");

    public Uri KeycloakBaseUrl =>
        new($"http://{_keycloak.Hostname}:{_keycloak.GetMappedPublicPort(KeycloakInternalPort)}/");

    public KeycloakTokenClient CreateTokenClient() =>
        new(KeycloakBaseUrl, KeycloakRealm);

    public ControlPlaneClient CreateControlPlaneClient(KeycloakTokenClient? tokens = null) =>
        new(ControlPlaneRestUrl, tokens ?? CreateTokenClient());

    // ─── Lifecycle ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder()
            .WithName($"clustral-e2e-{Guid.NewGuid():N}")
            .Build();
        await _network.CreateAsync();

        // Generate CA cert/key for ControlPlane mTLS — mounted into the container.
        _tempCaDir = Path.Combine(Path.GetTempPath(), $"clustral-e2e-ca-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempCaDir);
        _caCertPath = Path.Combine(_tempCaDir, "ca.crt");
        _caKeyPath = Path.Combine(_tempCaDir, "ca.key");
        GenerateTestCA(_caCertPath, _caKeyPath);

        // Build images and start infrastructure in parallel.
        _controlPlaneImage = BuildControlPlaneImage();
        _agentImage = BuildAgentImage();

        _mongo = BuildMongoContainer();
        _keycloak = BuildKeycloakContainer();
        _k3s = BuildK3sContainer();

        await Task.WhenAll(
            _controlPlaneImage.CreateAsync(),
            _agentImage.CreateAsync(),
            _mongo.StartAsync(),
            _keycloak.StartAsync(),
            _k3s.StartAsync());

        // Capture K3s kubeconfig for tests that want to verify k8s state directly.
        K3sKubeconfig = await _k3s.GetKubeconfigAsync();

        // Now start ControlPlane (depends on Mongo + Keycloak).
        _controlPlane = BuildControlPlaneContainer();
        await _controlPlane.StartAsync();

        // Wait for ControlPlane to be reachable.
        await WaitForControlPlaneReadyAsync(TimeSpan.FromMinutes(1));
    }

    public async Task DisposeAsync()
    {
        async Task SafeDispose(IAsyncDisposable? d)
        {
            if (d is null) return;
            try { await d.DisposeAsync(); } catch { /* best-effort */ }
        }

        await SafeDispose(_controlPlane);
        await SafeDispose(_k3s);
        await SafeDispose(_keycloak);
        await SafeDispose(_mongo);
        await SafeDispose(_network);

        if (Directory.Exists(_tempCaDir))
        {
            try { Directory.Delete(_tempCaDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ─── Per-test agent management ────────────────────────────────────────

    /// <summary>
    /// Starts a Go agent container with the given cluster ID and bootstrap token.
    /// The agent connects to the ControlPlane gRPC tunnel and the K3s API server.
    /// Disposing the returned <see cref="AgentHandle"/> stops and removes the container.
    /// </summary>
    public async Task<AgentHandle> StartAgentAsync(
        Guid clusterId,
        string bootstrapToken,
        AgentRuntimeOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new AgentRuntimeOptions();

        var builder = new ContainerBuilder()
            .WithImage(_agentImage)
            .WithName($"clustral-agent-e2e-{Guid.NewGuid():N}")
            .WithNetwork(_network)
            .WithEnvironment("AGENT_CLUSTER_ID", clusterId.ToString())
            .WithEnvironment("AGENT_CONTROL_PLANE_URL", $"https://{ControlPlaneAlias}:{ControlPlaneGrpcPort}")
            .WithEnvironment("AGENT_BOOTSTRAP_TOKEN", bootstrapToken)
            .WithEnvironment("AGENT_KUBERNETES_API_URL", $"https://{K3sAlias}:{K3sInternalPort}")
            .WithEnvironment("AGENT_KUBERNETES_SKIP_TLS_VERIFY", "true")
            .WithEnvironment("AGENT_HEARTBEAT_INTERVAL", "5s")
            .WithEnvironment("AGENT_RECONNECT_INITIAL_DELAY", "1s")
            .WithEnvironment("AGENT_RENEWAL_CHECK_INTERVAL", options.RenewalCheckInterval)
            .WithEnvironment("AGENT_CERT_RENEW_THRESHOLD", options.CertRenewThreshold)
            .WithEnvironment("AGENT_JWT_RENEW_THRESHOLD", options.JwtRenewThreshold)
            .WithCleanUp(true);

        var container = builder.Build();
        await container.StartAsync(ct);
        return new AgentHandle(container);
    }

    // ─── Container builders ───────────────────────────────────────────────

    private MongoDbContainer BuildMongoContainer() =>
        new MongoDbBuilder()
            .WithImage("mongo:8")
            .WithNetwork(_network)
            .WithNetworkAliases(MongoAlias)
            .WithCleanUp(true)
            .Build();

    private IContainer BuildKeycloakContainer()
    {
        var realmFile = Path.GetFullPath(Path.Combine(
            CommonDirectoryPath.GetSolutionDirectory().DirectoryPath,
            "infra", "keycloak", "clustral-realm.json"));

        return new ContainerBuilder()
            .WithImage("quay.io/keycloak/keycloak:24.0")
            .WithNetwork(_network)
            .WithNetworkAliases(KeycloakAlias)
            .WithEnvironment("KEYCLOAK_ADMIN", "admin")
            .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
            .WithEnvironment("KC_HEALTH_ENABLED", "true")
            .WithEnvironment("KC_HOSTNAME_STRICT", "false")
            .WithEnvironment("KC_HOSTNAME_STRICT_BACKCHANNEL", "false")
            .WithEnvironment("KC_HTTP_ENABLED", "true")
            .WithEnvironment("KC_HOSTNAME_URL", $"http://{KeycloakAlias}:{KeycloakInternalPort}")
            // The realm import only triggers on first boot when --import-realm is passed
            // and the file is in /opt/keycloak/data/import/.
            .WithResourceMapping(
                new FileInfo(realmFile),
                new FileInfo("/opt/keycloak/data/import/clustral-realm.json"),
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithCommand("start-dev", "--import-realm")
            .WithPortBinding(KeycloakInternalPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(req => req
                    .ForPath("/realms/clustral/.well-known/openid-configuration")
                    .ForPort(KeycloakInternalPort)))
            .WithCleanUp(true)
            .Build();
    }

    private K3sContainer BuildK3sContainer() =>
        new K3sBuilder()
            .WithNetwork(_network)
            .WithNetworkAliases(K3sAlias)
            .WithCleanUp(true)
            .Build();

    private IFutureDockerImage BuildControlPlaneImage() =>
        new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory(), string.Empty)
            .WithDockerfile("src/Clustral.ControlPlane/Dockerfile")
            .WithName($"clustral-controlplane-e2e:{Guid.NewGuid():N}")
            .WithBuildArgument("VERSION", "0.0.0-e2e")
            .WithCleanUp(true)
            .WithDeleteIfExists(true)
            .Build();

    private IFutureDockerImage BuildAgentImage() =>
        new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory(), "src/clustral-agent")
            .WithDockerfile("Dockerfile")
            .WithName($"clustral-agent-e2e:{Guid.NewGuid():N}")
            .WithBuildArgument("VERSION", "0.0.0-e2e")
            .WithCleanUp(true)
            .WithDeleteIfExists(true)
            .Build();

    private IContainer BuildControlPlaneContainer() =>
        new ContainerBuilder()
            .WithImage(_controlPlaneImage)
            .WithNetwork(_network)
            .WithNetworkAliases(ControlPlaneAlias)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("MongoDB__ConnectionString", $"mongodb://{MongoAlias}:{MongoInternalPort}")
            .WithEnvironment("MongoDB__DatabaseName", $"clustral-e2e-{Guid.NewGuid():N}")
            .WithEnvironment("Oidc__Authority", $"http://{KeycloakAlias}:{KeycloakInternalPort}/realms/{KeycloakRealm}")
            .WithEnvironment("Oidc__MetadataAddress",
                $"http://{KeycloakAlias}:{KeycloakInternalPort}/realms/{KeycloakRealm}/.well-known/openid-configuration")
            .WithEnvironment("Oidc__ClientId", "clustral-control-plane")
            .WithEnvironment("Oidc__Audience", "clustral-control-plane")
            .WithEnvironment("Oidc__RequireHttpsMetadata", "false")
            .WithEnvironment("CertificateAuthority__CaCertPath", "/etc/clustral/ca.crt")
            .WithEnvironment("CertificateAuthority__CaKeyPath", "/etc/clustral/ca.key")
            .WithEnvironment("CertificateAuthority__ClientCertValidityDays", "1")
            .WithEnvironment("CertificateAuthority__JwtValidityDays", "1")
            .WithResourceMapping(
                new FileInfo(_caCertPath),
                new FileInfo("/etc/clustral/ca.crt"),
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(
                new FileInfo(_caKeyPath),
                new FileInfo("/etc/clustral/ca.key"),
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithPortBinding(ControlPlaneRestPort, assignRandomHostPort: true)
            .WithPortBinding(ControlPlaneGrpcPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(req => req
                    .ForPath("/healthz/ready")
                    .ForPort(ControlPlaneRestPort)))
            .WithCleanUp(true)
            .Build();

    // ─── Helpers ──────────────────────────────────────────────────────────

    private async Task WaitForControlPlaneReadyAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { BaseAddress = ControlPlaneRestUrl };
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync("healthz/ready");
                if (response.IsSuccessStatusCode) return;
            }
            catch
            {
                // Transient — keep polling.
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"ControlPlane did not become ready within {timeout}");
    }

    private static void GenerateTestCA(string certPath, string keyPath)
    {
        using var caKey = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Clustral E2E Test CA",
            caKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        using var caCert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(10));

        File.WriteAllText(certPath, caCert.ExportCertificatePem());
        File.WriteAllText(keyPath, caKey.ExportPkcs8PrivateKeyPem());
    }
}

/// <summary>
/// Configuration for an agent container started by <see cref="E2EFixture.StartAgentAsync"/>.
/// All durations are Go-style strings (e.g. "2s", "8760h").
/// </summary>
public sealed record AgentRuntimeOptions(
    string RenewalCheckInterval = "6h",
    string CertRenewThreshold = "720h",
    string JwtRenewThreshold = "168h");

/// <summary>
/// Wrapper around an agent container that disposes the container on cleanup.
/// </summary>
public sealed class AgentHandle(IContainer container) : IAsyncDisposable
{
    public IContainer Container { get; } = container;

    public Task<string> DumpLogsAsync(CancellationToken ct = default) =>
        AgentLogReader.DumpAsync(Container, ct);

    public async ValueTask DisposeAsync()
    {
        try { await Container.StopAsync(); } catch { /* best-effort */ }
        try { await Container.DisposeAsync(); } catch { /* best-effort */ }
    }
}
