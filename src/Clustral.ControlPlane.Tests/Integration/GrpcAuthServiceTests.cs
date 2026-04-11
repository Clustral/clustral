using Clustral.V1;
using FluentAssertions;
using Grpc.Net.Client;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

/// <summary>
/// Integration tests for the AuthService gRPC endpoint.
/// Tests credential issuance, validation, agent bootstrap, rotation, and revocation.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class GrpcAuthServiceTests(
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

    private async Task<(string ClusterId, string BootstrapToken)> RegisterClusterAsync(string? name = null)
    {
        var clusterSvc = new ClusterService.ClusterServiceClient(_channel);
        var resp = await clusterSvc.RegisterAsync(new RegisterClusterRequest
        {
            Name = name ?? $"grpc-auth-{Guid.NewGuid().ToString()[..8]}",
        });
        return (resp.ClusterId, resp.BootstrapToken);
    }

    // ── IssueKubeconfigCredential ───────────────────────────────────────────

    [Fact]
    public async Task IssueKubeconfigCredential_ReturnsTokenAndExpiry()
    {
        var svc = new AuthService.AuthServiceClient(_channel);
        var (clusterId, _) = await RegisterClusterAsync();

        var response = await svc.IssueKubeconfigCredentialAsync(
            new IssueKubeconfigCredentialRequest
            {
                ClusterId = clusterId,
                OidcAccessToken = "test-oidc-token",
            });

        output.WriteLine($"Credential: id={response.CredentialId}, token={response.Token[..8]}...");
        output.WriteLine($"Expires: {response.ExpiresAt}");

        response.CredentialId.Should().NotBeNullOrEmpty();
        response.Token.Should().NotBeNullOrEmpty();
        response.IssuedAt.Should().NotBeNull();
        response.ExpiresAt.Should().NotBeNull();
        Guid.TryParse(response.CredentialId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task IssueKubeconfigCredential_MissingOidcToken_ThrowsInvalidArgument()
    {
        var svc = new AuthService.AuthServiceClient(_channel);
        var (clusterId, _) = await RegisterClusterAsync();

        var act = () => svc.IssueKubeconfigCredentialAsync(
            new IssueKubeconfigCredentialRequest
            {
                ClusterId = clusterId,
                OidcAccessToken = "",
            }).ResponseAsync;

        var ex = await act.Should().ThrowAsync<Grpc.Core.RpcException>();
        ex.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task IssueKubeconfigCredential_ClusterNotFound_ThrowsNotFound()
    {
        var svc = new AuthService.AuthServiceClient(_channel);

        var act = () => svc.IssueKubeconfigCredentialAsync(
            new IssueKubeconfigCredentialRequest
            {
                ClusterId = Guid.NewGuid().ToString(),
                OidcAccessToken = "test-token",
            }).ResponseAsync;

        var ex = await act.Should().ThrowAsync<Grpc.Core.RpcException>();
        ex.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.NotFound);
    }

    // ── ValidateKubeconfigCredential ─────────────────────────────────────────

    [Fact]
    public async Task ValidateKubeconfigCredential_ValidToken_ReturnsValid()
    {
        var svc = new AuthService.AuthServiceClient(_channel);
        var (clusterId, _) = await RegisterClusterAsync();

        var issued = await svc.IssueKubeconfigCredentialAsync(
            new IssueKubeconfigCredentialRequest
            {
                ClusterId = clusterId,
                OidcAccessToken = "test-oidc",
            });

        var validation = await svc.ValidateKubeconfigCredentialAsync(
            new ValidateKubeconfigCredentialRequest
            {
                ClusterId = clusterId,
                Token = issued.Token,
            });

        output.WriteLine($"Valid: {validation.Valid}, CredentialId: {validation.CredentialId}");
        validation.Valid.Should().BeTrue();
        validation.CredentialId.Should().Be(issued.CredentialId);
    }

    [Fact]
    public async Task ValidateKubeconfigCredential_InvalidToken_ReturnsInvalid()
    {
        var svc = new AuthService.AuthServiceClient(_channel);
        var (clusterId, _) = await RegisterClusterAsync();

        var validation = await svc.ValidateKubeconfigCredentialAsync(
            new ValidateKubeconfigCredentialRequest
            {
                ClusterId = clusterId,
                Token = "bogus-token",
            });

        output.WriteLine($"Valid: {validation.Valid}, Reason: {validation.Reason}");
        validation.Valid.Should().BeFalse();
        validation.Reason.Should().Be(InvalidationReason.NotFound);
    }

    [Fact]
    public async Task ValidateKubeconfigCredential_WrongCluster_ReturnsInvalid()
    {
        var svc = new AuthService.AuthServiceClient(_channel);
        var (clusterId, _) = await RegisterClusterAsync();

        var issued = await svc.IssueKubeconfigCredentialAsync(
            new IssueKubeconfigCredentialRequest
            {
                ClusterId = clusterId,
                OidcAccessToken = "test-oidc",
            });

        var validation = await svc.ValidateKubeconfigCredentialAsync(
            new ValidateKubeconfigCredentialRequest
            {
                ClusterId = Guid.NewGuid().ToString(),
                Token = issued.Token,
            });

        output.WriteLine($"Wrong cluster: Valid={validation.Valid}, Reason={validation.Reason}");
        validation.Valid.Should().BeFalse();
        validation.Reason.Should().Be(InvalidationReason.WrongCluster);
    }

    // ── IssueAgentCredential (bootstrap) ─────────────────────────────────────

    [Fact]
    public async Task IssueAgentCredential_ValidBootstrapToken_ReturnsCredential()
    {
        var svc = new AuthService.AuthServiceClient(_channel);
        var (clusterId, bootstrapToken) = await RegisterClusterAsync();

        var response = await svc.IssueAgentCredentialAsync(
            new IssueAgentCredentialRequest
            {
                ClusterId = clusterId,
                BootstrapToken = bootstrapToken,
            });

        output.WriteLine($"Agent credential: id={response.CredentialId}, token={response.Token[..8]}...");
        response.CredentialId.Should().NotBeNullOrEmpty();
        response.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task IssueAgentCredential_BootstrapTokenConsumed_SecondCallFails()
    {
        var svc = new AuthService.AuthServiceClient(_channel);
        var (clusterId, bootstrapToken) = await RegisterClusterAsync();

        // First call succeeds.
        await svc.IssueAgentCredentialAsync(new IssueAgentCredentialRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        });

        // Second call fails — token consumed.
        var act = () => svc.IssueAgentCredentialAsync(new IssueAgentCredentialRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        }).ResponseAsync;

        var ex = await act.Should().ThrowAsync<Grpc.Core.RpcException>();
        ex.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.Unauthenticated);
        output.WriteLine("Bootstrap token correctly consumed (single-use)");
    }

    // ── RotateAgentCredential ────────────────────────────────────────────────

    [Fact]
    public async Task RotateAgentCredential_ReplacesOldWithNew()
    {
        var svc = new AuthService.AuthServiceClient(_channel);
        var (clusterId, bootstrapToken) = await RegisterClusterAsync();

        var agentCred = await svc.IssueAgentCredentialAsync(new IssueAgentCredentialRequest
        {
            ClusterId = clusterId,
            BootstrapToken = bootstrapToken,
        });

        var rotated = await svc.RotateAgentCredentialAsync(new RotateAgentCredentialRequest
        {
            ClusterId = clusterId,
            CurrentToken = agentCred.Token,
        });

        output.WriteLine($"Old: {agentCred.CredentialId}, New: {rotated.CredentialId}");
        rotated.CredentialId.Should().NotBe(agentCred.CredentialId);
        rotated.Token.Should().NotBe(agentCred.Token);
    }

    // ── RevokeCredential ─────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeCredential_MarksAsRevoked()
    {
        var svc = new AuthService.AuthServiceClient(_channel);
        var (clusterId, _) = await RegisterClusterAsync();

        var issued = await svc.IssueKubeconfigCredentialAsync(
            new IssueKubeconfigCredentialRequest
            {
                ClusterId = clusterId,
                OidcAccessToken = "test-oidc",
            });

        var revoke = await svc.RevokeCredentialAsync(new RevokeCredentialRequest
        {
            CredentialId = issued.CredentialId,
            Reason = "test revocation",
        });

        output.WriteLine($"Revoked: {revoke.Revoked}");
        revoke.Revoked.Should().BeTrue();

        // Validate after revocation — should be invalid.
        var validation = await svc.ValidateKubeconfigCredentialAsync(
            new ValidateKubeconfigCredentialRequest
            {
                ClusterId = clusterId,
                Token = issued.Token,
            });

        validation.Valid.Should().BeFalse();
        validation.Reason.Should().Be(InvalidationReason.Revoked);
        output.WriteLine("Revoked credential correctly invalidated");
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
