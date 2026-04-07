using Clustral.V1;
using FluentAssertions;
using Grpc.Net.Client;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

/// <summary>
/// Integration tests for the ClusterService gRPC endpoint.
/// Uses WebApplicationFactory with Testcontainers MongoDB.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class GrpcClusterServiceTests(
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

    // ── Register ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_ReturnsClusterIdAndBootstrapToken()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);
        var name = $"grpc-reg-{Guid.NewGuid().ToString()[..8]}";

        var response = await svc.RegisterAsync(new RegisterClusterRequest
        {
            Name = name,
            Description = "gRPC test cluster",
            AgentPublicKeyPem = "test-pem-key",
        });

        output.WriteLine($"Registered: id={response.ClusterId}, token={response.BootstrapToken[..8]}...");
        response.ClusterId.Should().NotBeNullOrEmpty();
        response.BootstrapToken.Should().NotBeNullOrEmpty();
        Guid.TryParse(response.ClusterId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Register_DuplicateName_ThrowsAlreadyExists()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);
        var name = $"grpc-dup-{Guid.NewGuid().ToString()[..8]}";

        await svc.RegisterAsync(new RegisterClusterRequest
        {
            Name = name,
            AgentPublicKeyPem = "key",
        });

        var act = () => svc.RegisterAsync(new RegisterClusterRequest
        {
            Name = name,
            AgentPublicKeyPem = "key",
        }).ResponseAsync;

        var ex = await act.Should().ThrowAsync<Grpc.Core.RpcException>();
        ex.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.AlreadyExists);
        output.WriteLine($"Duplicate name rejected: {ex.Which.Status.Detail}");
    }

    [Fact]
    public async Task Register_EmptyName_ThrowsInvalidArgument()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);

        var act = () => svc.RegisterAsync(new RegisterClusterRequest
        {
            Name = "",
            AgentPublicKeyPem = "key",
        }).ResponseAsync;

        var ex = await act.Should().ThrowAsync<Grpc.Core.RpcException>();
        ex.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.InvalidArgument);
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsRegisteredClusters()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);
        var name = $"grpc-list-{Guid.NewGuid().ToString()[..8]}";

        await svc.RegisterAsync(new RegisterClusterRequest
        {
            Name = name,
            AgentPublicKeyPem = "key",
        });

        var response = await svc.ListAsync(new ListClustersRequest { PageSize = 100 });

        output.WriteLine($"Clusters: {response.Clusters.Count}");
        response.Clusters.Should().Contain(c => c.Name == name);
    }

    // ── Get ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsCluster()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);
        var name = $"grpc-get-{Guid.NewGuid().ToString()[..8]}";

        var reg = await svc.RegisterAsync(new RegisterClusterRequest
        {
            Name = name,
            Description = "test desc",
            AgentPublicKeyPem = "key",
        });

        var cluster = await svc.GetAsync(new GetClusterRequest { ClusterId = reg.ClusterId });

        output.WriteLine($"Get: {cluster.Name} ({cluster.Status})");
        cluster.Name.Should().Be(name);
        cluster.Description.Should().Be("test desc");
        cluster.Status.Should().Be(ClusterStatus.Pending);
    }

    [Fact]
    public async Task Get_NotFound_ThrowsNotFound()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);

        var act = () => svc.GetAsync(new GetClusterRequest
        {
            ClusterId = Guid.NewGuid().ToString(),
        }).ResponseAsync;

        var ex = await act.Should().ThrowAsync<Grpc.Core.RpcException>();
        ex.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.NotFound);
    }

    // ── UpdateStatus ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStatus_ChangesClusterStatus()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);
        var name = $"grpc-status-{Guid.NewGuid().ToString()[..8]}";

        var reg = await svc.RegisterAsync(new RegisterClusterRequest
        {
            Name = name,
            AgentPublicKeyPem = "key",
        });

        await svc.UpdateStatusAsync(new UpdateClusterStatusRequest
        {
            ClusterId = reg.ClusterId,
            Status = ClusterStatus.Connected,
            KubernetesVersion = "v1.29.0",
        });

        var cluster = await svc.GetAsync(new GetClusterRequest { ClusterId = reg.ClusterId });

        output.WriteLine($"Status: {cluster.Status}, K8s: {cluster.KubernetesVersion}");
        cluster.Status.Should().Be(ClusterStatus.Connected);
        cluster.KubernetesVersion.Should().Be("v1.29.0");
    }

    // ── Deregister ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Deregister_RemovesCluster()
    {
        var svc = new ClusterService.ClusterServiceClient(_channel);
        var name = $"grpc-dereg-{Guid.NewGuid().ToString()[..8]}";

        var reg = await svc.RegisterAsync(new RegisterClusterRequest
        {
            Name = name,
            AgentPublicKeyPem = "key",
        });

        await svc.DeregisterAsync(new DeregisterClusterRequest { ClusterId = reg.ClusterId });

        var act = () => svc.GetAsync(new GetClusterRequest { ClusterId = reg.ClusterId }).ResponseAsync;
        var ex = await act.Should().ThrowAsync<Grpc.Core.RpcException>();
        ex.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.NotFound);
        output.WriteLine("Deregistered and confirmed not found");
    }

    public void Dispose() => _channel.Dispose();

    /// <summary>
    /// Required for gRPC over TestServer — the test server returns HTTP/1.1
    /// but gRPC client expects HTTP/2. This handler overrides the response
    /// version so the gRPC client doesn't reject it.
    /// </summary>
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
