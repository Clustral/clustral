using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography.X509Certificates;
using Clustral.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

/// <summary>
/// Integration tests for the mTLS + JWT agent authentication RPCs:
/// RegisterAgent, RenewCertificate, RenewToken.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class GrpcMtlsTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output) : IDisposable
{
    private readonly GrpcChannel _channel = CreateChannel(factory);

    private static GrpcChannel CreateChannel(ClustralWebApplicationFactory factory)
    {
        var client = factory.CreateDefaultClient(new ResponseVersionHandler());
        return GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = client,
        });
    }

    // ── RegisterAgent ───────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAgent_ValidBootstrapToken_ReturnsCertAndJwt()
    {
        var (clusterId, bootstrapToken) = await RegisterClusterAsync();

        var svc = new ClusterService.ClusterServiceClient(_channel);
        var response = await svc.RegisterAgentAsync(new RegisterAgentRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        });

        output.WriteLine($"Agent registered: clusterId={response.ClusterId}");

        response.ClusterId.Should().Be(clusterId);
        response.ClientCertificatePem.Should().Contain("-----BEGIN CERTIFICATE-----");
        response.ClientPrivateKeyPem.Should().Contain("-----BEGIN PRIVATE KEY-----");
        response.CaCertificatePem.Should().Contain("-----BEGIN CERTIFICATE-----");
        response.Jwt.Should().NotBeNullOrEmpty();
        response.CertExpiresAt.Should().NotBeNull();
        response.JwtExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterAgent_CertHasCorrectCN()
    {
        var (clusterId, bootstrapToken) = await RegisterClusterAsync();
        var svc = new ClusterService.ClusterServiceClient(_channel);

        var response = await svc.RegisterAgentAsync(new RegisterAgentRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        });

        var cert = X509Certificate2.CreateFromPem(response.ClientCertificatePem);
        cert.Subject.Should().Contain($"CN={clusterId}");

        output.WriteLine($"Cert subject: {cert.Subject}");
    }

    [Fact]
    public async Task RegisterAgent_JwtHasCorrectClaims()
    {
        var (clusterId, bootstrapToken) = await RegisterClusterAsync();
        var svc = new ClusterService.ClusterServiceClient(_channel);

        var response = await svc.RegisterAgentAsync(new RegisterAgentRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        });

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(response.Jwt);

        jwt.Claims.Should().Contain(c => c.Type == "agent_id" && c.Value == clusterId);
        jwt.Claims.Should().Contain(c => c.Type == "cluster_id" && c.Value == clusterId);
        jwt.Claims.Should().Contain(c => c.Type == "token_version");
        jwt.Claims.Should().Contain(c => c.Type == "allowed_rpcs");
        jwt.Issuer.Should().Be("clustral-controlplane");

        output.WriteLine($"JWT claims: {string.Join(", ", jwt.Claims.Select(c => $"{c.Type}={c.Value}"))}");
    }

    [Fact]
    public async Task RegisterAgent_InvalidBootstrapToken_ThrowsUnauthenticated()
    {
        var (clusterId, _) = await RegisterClusterAsync();
        var svc = new ClusterService.ClusterServiceClient(_channel);

        var act = async () => await svc.RegisterAgentAsync(new RegisterAgentRequest
        {
            ClusterId = clusterId,
            BootstrapToken = "wrong-token",
        });

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task RegisterAgent_ConsumedToken_ThrowsFailedPrecondition()
    {
        var (clusterId, bootstrapToken) = await RegisterClusterAsync();
        var svc = new ClusterService.ClusterServiceClient(_channel);

        // First call succeeds.
        await svc.RegisterAgentAsync(new RegisterAgentRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        });

        // Second call fails — token consumed.
        var act = async () => await svc.RegisterAgentAsync(new RegisterAgentRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        });

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task RegisterAgent_ClusterNotFound_ThrowsNotFound()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);

        var act = async () => await svc.RegisterAgentAsync(new RegisterAgentRequest
        {
            ClusterId = Guid.NewGuid().ToString(),
            BootstrapToken = "any-token",
        });

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    // ── RenewCertificate ────────────────────────────────────────────────────

    [Fact]
    public async Task RenewCertificate_ReturnsNewCert()
    {
        var (clusterId, bootstrapToken) = await RegisterClusterAsync();
        var svc = new ClusterService.ClusterServiceClient(_channel);

        // Register agent first.
        await svc.RegisterAgentAsync(new RegisterAgentRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        });

        // Renew certificate.
        var response = await svc.RenewCertificateAsync(new RenewCertificateRequest
        {
            ClusterId = clusterId,
        });

        response.ClientCertificatePem.Should().Contain("-----BEGIN CERTIFICATE-----");
        response.ClientPrivateKeyPem.Should().Contain("-----BEGIN PRIVATE KEY-----");
        response.ExpiresAt.Should().NotBeNull();

        var cert = X509Certificate2.CreateFromPem(response.ClientCertificatePem);
        cert.NotAfter.Should().BeAfter(DateTime.UtcNow.AddDays(390));

        output.WriteLine($"Renewed cert expires: {cert.NotAfter}");
    }

    // ── RenewToken ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RenewToken_ReturnsNewJwt()
    {
        var (clusterId, bootstrapToken) = await RegisterClusterAsync();
        var svc = new ClusterService.ClusterServiceClient(_channel);

        // Register agent first.
        var reg = await svc.RegisterAgentAsync(new RegisterAgentRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        });

        // Renew token.
        var response = await svc.RenewTokenAsync(new RenewTokenRequest
        {
            ClusterId = clusterId,
        });

        response.Jwt.Should().NotBeNullOrEmpty();
        response.ExpiresAt.Should().NotBeNull();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(response.Jwt);
        jwt.Claims.Should().Contain(c => c.Type == "agent_id" && c.Value == clusterId);

        output.WriteLine($"Renewed JWT expires: {response.ExpiresAt}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string ClusterId, string BootstrapToken)> RegisterClusterAsync()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);
        var name = $"mtls-test-{Guid.NewGuid().ToString()[..8]}";

        var response = await svc.RegisterAsync(new RegisterClusterRequest
        {
            Name = name,
            Description = "mTLS test cluster",
        });

        return (response.ClusterId, response.BootstrapToken);
    }

    public void Dispose() => _channel.Dispose();

    private sealed class ResponseVersionHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            response.Version = request.Version;
            return response;
        }
    }
}
