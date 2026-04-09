using Clustral.E2E.Tests.Fixtures;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.E2E.Tests.Tests;

/// <summary>
/// Validates the full agent bootstrap path: cluster registration via REST,
/// agent container startup with the bootstrap token, mTLS handshake against
/// the ControlPlane gRPC port, and tunnel establishment.
///
/// If this test passes, the entire e2e fixture (Keycloak auth, image builds,
/// Docker network, container wiring) is wired correctly.
/// </summary>
[Collection(E2ETestCollection.Name)]
[Trait("Category", "E2E")]
public sealed class AgentBootstrapTests(E2EFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RegisterCluster_DeployAgent_TunnelEstablished()
    {
        // Arrange — authenticate as admin via Keycloak password grant.
        using var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        // Act — register a new cluster, then start the Go agent with its bootstrap token.
        var registration = await cp.RegisterClusterAsync($"e2e-bootstrap-{Guid.NewGuid():N}");
        output.WriteLine($"Registered cluster {registration.ClusterId}");

        await using var agent = await fixture.StartAgentAsync(
            registration.ClusterId,
            registration.BootstrapToken);

        // Assert — the cluster's status should transition to Connected once the
        // agent finishes the mTLS bootstrap and opens the tunnel.
        ClusterDto cluster;
        try
        {
            cluster = await cp.WaitForClusterStatusAsync(
                registration.ClusterId,
                expectedStatus: "Connected",
                timeout: TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            output.WriteLine(await agent.DumpLogsAsync());
            throw;
        }

        cluster.Status.Should().Be("Connected");
        cluster.KubernetesVersion.Should().NotBeNullOrEmpty(
            "the agent reports the K3s API version as part of AgentHello");
        cluster.AgentVersion.Should().NotBeNullOrEmpty();
    }
}
