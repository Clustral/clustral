using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Testcontainers.K3s;

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
    public const string RabbitMqAlias = "rabbitmq";
    public const string K3sAlias = "k3s";
    public const string ApiGatewayAlias = "api-gateway";
    public const string ControlPlaneAlias = "controlplane";
    public const string KeycloakRealm = "clustral";

    // Internal ports as exposed by each service.
    private const int KeycloakInternalPort = 8080;
    private const int ControlPlaneRestPort = 5100;
    private const int ControlPlaneGrpcPort = 5443;
    private const int K3sInternalPort = 6443;
    private const int MongoInternalPort = 27017;
    private const int ApiGatewayRestPort = 8080;
    private const int ApiGatewayGrpcPort = 5443;
    private const int RabbitMqInternalPort = 5672;

    private INetwork _network = null!;
    private IContainer _mongo = null!;
    private IContainer _rabbitmq = null!;
    private IContainer _keycloak = null!;
    private K3sContainer _k3s = null!;
    private IContainer _apiGateway = null!;
    private IContainer _controlPlane = null!;
    private IFutureDockerImage _controlPlaneImage = null!;
    private IFutureDockerImage _apiGatewayImage = null!;
    private IFutureDockerImage _agentImage = null!;

    private string _tempCaDir = null!;
    private string _caCertPath = null!;
    private string _caKeyPath = null!;
    private string _internalJwtPrivateKeyPath = null!;
    private string _internalJwtPublicKeyPath = null!;
    private string _kubeconfigJwtPrivateKeyPath = null!;
    private string _kubeconfigJwtPublicKeyPath = null!;
    private string _k3sSaTokenPath = null!;
    private string _k3sCaCertFilePath = null!;

    public string K3sKubeconfig { get; private set; } = string.Empty;

    public Uri ControlPlaneRestUrl =>
        new($"http://{_apiGateway.Hostname}:{_apiGateway.GetMappedPublicPort(ApiGatewayRestPort)}");

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

        // Generate ES256 key pairs for internal-jwt (gateway→downstream) and
        // kubeconfig-jwt (ControlPlane-signed kubeconfig credentials).
        // Both key pairs are mounted into the matching containers so the
        // gateway can sign + validate kubeconfig JWTs, and the ControlPlane
        // can validate internal JWTs + sign kubeconfig JWTs.
        _internalJwtPrivateKeyPath = Path.Combine(_tempCaDir, "internal-jwt-private.pem");
        _internalJwtPublicKeyPath = Path.Combine(_tempCaDir, "internal-jwt-public.pem");
        _kubeconfigJwtPrivateKeyPath = Path.Combine(_tempCaDir, "kubeconfig-jwt-private.pem");
        _kubeconfigJwtPublicKeyPath = Path.Combine(_tempCaDir, "kubeconfig-jwt-public.pem");
        GenerateEs256KeyPair(_internalJwtPrivateKeyPath, _internalJwtPublicKeyPath);
        GenerateEs256KeyPair(_kubeconfigJwtPrivateKeyPath, _kubeconfigJwtPublicKeyPath);

        // Build images and start infrastructure in parallel.
        _apiGatewayImage = BuildApiGatewayImage();
        _controlPlaneImage = BuildControlPlaneImage();
        _agentImage = BuildAgentImage();

        _mongo = BuildMongoContainer();
        _rabbitmq = BuildRabbitMqContainer();
        _keycloak = BuildKeycloakContainer();
        _k3s = BuildK3sContainer();

        await Task.WhenAll(
            _apiGatewayImage.CreateAsync(),
            _controlPlaneImage.CreateAsync(),
            _agentImage.CreateAsync(),
            _mongo.StartAsync(),
            _rabbitmq.StartAsync(),
            _keycloak.StartAsync(),
            _k3s.StartAsync());

        // Capture K3s kubeconfig for tests that want to verify k8s state directly.
        K3sKubeconfig = await _k3s.GetKubeconfigAsync();

        // Provision a ServiceAccount in K3s and extract its token + CA cert.
        // The agent reads /var/run/secrets/kubernetes.io/serviceaccount/{token,ca.crt}
        // — these files get bind-mounted into per-test agent containers.
        await ProvisionK3sServiceAccountAsync();

        // Now start ControlPlane (depends on Mongo + Keycloak).
        _controlPlane = BuildControlPlaneContainer();
        await _controlPlane.StartAsync();

        // Start API Gateway (depends on ControlPlane).
        _apiGateway = BuildApiGatewayContainer();
        await _apiGateway.StartAsync();

        // Wait for gateway to be reachable (which means ControlPlane is also ready).
        await WaitForGatewayReadyAsync(TimeSpan.FromMinutes(1));
    }

    public async Task DisposeAsync()
    {
        async Task SafeDispose(IAsyncDisposable? d)
        {
            if (d is null) return;
            try { await d.DisposeAsync(); } catch { /* best-effort */ }
        }

        await SafeDispose(_apiGateway);
        await SafeDispose(_controlPlane);
        await SafeDispose(_k3s);
        await SafeDispose(_keycloak);
        await SafeDispose(_rabbitmq);
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
            // Mount the K3s SA token + CA cert at the in-cluster path so the agent's
            // saTokenRoundTripper can authenticate to K3s. Without this, the agent
            // gets 401 from K3s because it has no credentials.
            .WithBindMount(_k3sSaTokenPath, "/var/run/secrets/kubernetes.io/serviceaccount/token", AccessMode.ReadOnly)
            .WithBindMount(_k3sCaCertFilePath, "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt", AccessMode.ReadOnly)
            .WithCleanUp(true);

        var container = builder.Build();
        await container.StartAsync(ct);
        return new AgentHandle(container);
    }

    // ─── Container builders ───────────────────────────────────────────────

    private IContainer BuildMongoContainer() =>
        // Use a plain ContainerBuilder (not MongoDbBuilder) so MongoDB starts
        // without authentication. The container is on an isolated Docker network,
        // only the ControlPlane talks to it, and the connection string in
        // BuildControlPlaneContainer doesn't include credentials.
        new ContainerBuilder()
            .WithImage("mongo:8")
            .WithNetwork(_network)
            .WithNetworkAliases(MongoAlias)
            .WithPortBinding(MongoInternalPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(MongoInternalPort))
            .WithCleanUp(true)
            .Build();

    private IContainer BuildRabbitMqContainer() =>
        new ContainerBuilder()
            .WithImage("rabbitmq:4")
            .WithNetwork(_network)
            .WithNetworkAliases(RabbitMqAlias)
            .WithPortBinding(RabbitMqInternalPort, assignRandomHostPort: true)
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "clustral")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", "clustral")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(RabbitMqInternalPort))
            .WithCleanUp(true)
            .Build();

    private IFutureDockerImage BuildApiGatewayImage() =>
        new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(GetRepoRootPath(), string.Empty)
            .WithDockerfile("src/Clustral.ApiGateway/Dockerfile")
            .WithName($"clustral-api-gateway-e2e:{Guid.NewGuid():N}")
            .WithBuildArgument("VERSION", "0.0.0-e2e")
            .WithCleanUp(true)
            .WithDeleteIfExists(true)
            .Build();

    private IContainer BuildApiGatewayContainer() =>
        new ContainerBuilder()
            .WithImage(_apiGatewayImage)
            .WithNetwork(_network)
            .WithNetworkAliases(ApiGatewayAlias)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("Oidc__Authority", $"http://{KeycloakAlias}:{KeycloakInternalPort}/realms/{KeycloakRealm}")
            .WithEnvironment("Oidc__Audience", "clustral-control-plane")
            .WithEnvironment("Oidc__RequireHttpsMetadata", "false")
            .WithEnvironment("InternalJwt__PrivateKeyPath", "/etc/clustral/jwt/private.pem")
            .WithEnvironment("KubeconfigJwt__PublicKeyPath", "/etc/clustral/kubeconfig-jwt/public.pem")
            .WithEnvironment("ReverseProxy__Clusters__controlplane__Destinations__default__Address",
                $"http://{ControlPlaneAlias}:{ControlPlaneRestPort}")
            .WithResourceMapping(
                new FileInfo(_internalJwtPrivateKeyPath),
                new FileInfo("/etc/clustral/jwt/private.pem"),
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(
                new FileInfo(_kubeconfigJwtPublicKeyPath),
                new FileInfo("/etc/clustral/kubeconfig-jwt/public.pem"),
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithPortBinding(ApiGatewayRestPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(req => req
                    .ForPath("/gateway/healthz")
                    .ForPort(ApiGatewayRestPort)))
            .WithCleanUp(true)
            .Build();

    private IContainer BuildKeycloakContainer()
    {
        var realmFile = Path.GetFullPath(Path.Combine(
            GetRepoRootPath().DirectoryPath,
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
            .WithDockerfileDirectory(GetRepoRootPath(), string.Empty)
            .WithDockerfile("src/Clustral.ControlPlane/Dockerfile")
            .WithName($"clustral-controlplane-e2e:{Guid.NewGuid():N}")
            .WithBuildArgument("VERSION", "0.0.0-e2e")
            .WithCleanUp(true)
            .WithDeleteIfExists(true)
            .Build();

    private IFutureDockerImage BuildAgentImage() =>
        new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(GetRepoRootPath(), "src/clustral-agent")
            .WithDockerfile("Dockerfile")
            .WithName($"clustral-agent-e2e:{Guid.NewGuid():N}")
            .WithBuildArgument("VERSION", "0.0.0-e2e")
            .WithCleanUp(true)
            .WithDeleteIfExists(true)
            .Build();

    /// <summary>
    /// Finds the repository root by walking up from the current directory until
    /// it finds <c>Clustral.slnx</c>. Testcontainers' built-in
    /// <c>CommonDirectoryPath.GetSolutionDirectory()</c> only searches for
    /// <c>*.sln</c> files, which this repo doesn't have (we use the new
    /// <c>.slnx</c> XML solution format).
    /// </summary>
    private static CommonDirectoryPath GetRepoRootPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Clustral.slnx")))
            {
                return new CommonDirectoryPath(dir.FullName);
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate Clustral.slnx by walking up from " + AppContext.BaseDirectory);
    }

    private IContainer BuildControlPlaneContainer() =>
        new ContainerBuilder()
            .WithImage(_controlPlaneImage)
            .WithNetwork(_network)
            .WithNetworkAliases(ControlPlaneAlias)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("MongoDB__ConnectionString", $"mongodb://{MongoAlias}:{MongoInternalPort}")
            .WithEnvironment("MongoDB__DatabaseName", $"clustral-e2e-{Guid.NewGuid():N}")
            .WithEnvironment("RabbitMQ__Host", RabbitMqAlias)
            .WithEnvironment("RabbitMQ__Port", RabbitMqInternalPort.ToString())
            .WithEnvironment("RabbitMQ__User", "clustral")
            .WithEnvironment("RabbitMQ__Pass", "clustral")
            .WithEnvironment("CertificateAuthority__CaCertPath", "/etc/clustral/ca.crt")
            .WithEnvironment("CertificateAuthority__CaKeyPath", "/etc/clustral/ca.key")
            .WithEnvironment("CertificateAuthority__ClientCertValidityDays", "1")
            .WithEnvironment("CertificateAuthority__JwtValidityDays", "1")
            .WithEnvironment("InternalJwt__PublicKeyPath", "/etc/clustral/jwt/public.pem")
            .WithEnvironment("KubeconfigJwt__PrivateKeyPath", "/etc/clustral/kubeconfig-jwt/private.pem")
            .WithResourceMapping(
                new FileInfo(_caCertPath),
                new FileInfo("/etc/clustral/ca.crt"),
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(
                new FileInfo(_caKeyPath),
                new FileInfo("/etc/clustral/ca.key"),
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(
                new FileInfo(_internalJwtPublicKeyPath),
                new FileInfo("/etc/clustral/jwt/public.pem"),
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(
                new FileInfo(_kubeconfigJwtPrivateKeyPath),
                new FileInfo("/etc/clustral/kubeconfig-jwt/private.pem"),
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

    private async Task WaitForGatewayReadyAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { BaseAddress = ControlPlaneRestUrl };
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync("healthz");
                if (response.IsSuccessStatusCode) return;
            }
            catch { }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        throw new TimeoutException($"API Gateway did not become ready within {timeout}");
    }

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

    /// <summary>
    /// Creates a cluster-admin ServiceAccount in K3s and writes its token + CA cert
    /// to the temp directory so they can be bind-mounted into agent containers
    /// at <c>/var/run/secrets/kubernetes.io/serviceaccount/</c>.
    /// </summary>
    private async Task ProvisionK3sServiceAccountAsync()
    {
        const string sa = "clustral-agent";
        const string ns = "default";

        // Create SA + ClusterRoleBinding (cluster-admin so impersonation works).
        // Use kubectl inside the K3s container — it has the binary preinstalled.
        var manifest = $@"
apiVersion: v1
kind: ServiceAccount
metadata:
  name: {sa}
  namespace: {ns}
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: {sa}-admin
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: cluster-admin
subjects:
- kind: ServiceAccount
  name: {sa}
  namespace: {ns}
";

        // Write manifest into the K3s container and apply it.
        var manifestPath = "/tmp/clustral-agent-sa.yaml";
        await _k3s.CopyAsync(System.Text.Encoding.UTF8.GetBytes(manifest), manifestPath);

        var apply = await _k3s.ExecAsync(["kubectl", "apply", "-f", manifestPath]);
        if (apply.ExitCode != 0)
            throw new InvalidOperationException(
                $"kubectl apply failed: {apply.Stderr}");

        // Generate a token for the ServiceAccount (k8s 1.24+ — TokenRequest API).
        var tokenResult = await _k3s.ExecAsync(
            ["kubectl", "create", "token", sa, "-n", ns, "--duration=24h"]);
        if (tokenResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"kubectl create token failed: {tokenResult.Stderr}");

        var token = tokenResult.Stdout.Trim();

        // Extract the K3s CA cert from the kubeconfig (base64-encoded inline).
        var caCertB64 = ExtractKubeconfigCaCert(K3sKubeconfig);
        var caCertPem = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(caCertB64));

        _k3sSaTokenPath = Path.Combine(_tempCaDir, "k3s-sa-token");
        _k3sCaCertFilePath = Path.Combine(_tempCaDir, "k3s-ca.crt");

        await File.WriteAllTextAsync(_k3sSaTokenPath, token);
        await File.WriteAllTextAsync(_k3sCaCertFilePath, caCertPem);
    }

    private static string ExtractKubeconfigCaCert(string kubeconfigYaml)
    {
        // Find the certificate-authority-data line (YAML).
        const string key = "certificate-authority-data:";
        var idx = kubeconfigYaml.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
            throw new InvalidOperationException("certificate-authority-data not found in kubeconfig");

        var rest = kubeconfigYaml[(idx + key.Length)..];
        var endOfLine = rest.IndexOfAny(['\r', '\n']);
        var b64 = (endOfLine >= 0 ? rest[..endOfLine] : rest).Trim();
        return b64;
    }

    private static void GenerateEs256KeyPair(string privateKeyPath, string publicKeyPath)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(privateKeyPath, key.ExportECPrivateKeyPem());
        File.WriteAllText(publicKeyPath, key.ExportSubjectPublicKeyInfoPem());
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

        // SANs — must include the hostname agents use to connect.
        // The agent connects to https://controlplane:5443 (Docker network alias).
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("controlplane");
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

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
