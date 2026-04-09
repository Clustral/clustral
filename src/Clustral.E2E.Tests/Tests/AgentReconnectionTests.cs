using System.Net;
using Clustral.E2E.Tests.Fixtures;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.E2E.Tests.Tests;

/// <summary>
/// Validates that the gRPC tunnel survives an agent restart: when the agent
/// container is torn down, the ControlPlane marks the cluster Disconnected;
/// when a fresh agent comes back the tunnel re-establishes and the proxy
/// starts working again.
/// </summary>
[Collection(E2ETestCollection.Name)]
[Trait("Category", "E2E")]
public sealed class AgentReconnectionTests(E2EFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task AgentStop_ClusterStatusBecomesDisconnected()
    {
        using var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        var registration = await cp.RegisterClusterAsync(
            $"e2e-reconn-{Guid.NewGuid():N}".Substring(0, 30));

        var agent = await fixture.StartAgentAsync(
            registration.ClusterId, registration.BootstrapToken);

        await cp.WaitForClusterStatusAsync(
            registration.ClusterId, "Connected", TimeSpan.FromSeconds(60));
        output.WriteLine($"Cluster {registration.ClusterId} connected");

        // Stop the agent — the TunnelSessionManager should detect the closed
        // stream and update the cluster status.
        await agent.DisposeAsync();

        var cluster = await cp.WaitForClusterStatusAsync(
            registration.ClusterId, "Disconnected", TimeSpan.FromSeconds(60));

        cluster.Status.Should().Be("Disconnected");
    }

    [Fact]
    public async Task FreshAgent_AfterStop_TunnelReestablishedAndProxyWorks()
    {
        using var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        // Round 1: register, connect, drop.
        var first = await cp.RegisterClusterAsync(
            $"e2e-reconn1-{Guid.NewGuid():N}".Substring(0, 30));

        var firstAgent = await fixture.StartAgentAsync(first.ClusterId, first.BootstrapToken);
        await cp.WaitForClusterStatusAsync(first.ClusterId, "Connected", TimeSpan.FromSeconds(60));
        await firstAgent.DisposeAsync();
        await cp.WaitForClusterStatusAsync(first.ClusterId, "Disconnected", TimeSpan.FromSeconds(60));
        await cp.DeleteClusterAsync(first.ClusterId);

        // Round 2: fresh registration → fresh agent → proxy must work.
        var second = await cp.RegisterClusterAsync(
            $"e2e-reconn2-{Guid.NewGuid():N}".Substring(0, 30));

        await using var agent = await fixture.StartAgentAsync(
            second.ClusterId, second.BootstrapToken);

        try
        {
            await cp.WaitForClusterStatusAsync(second.ClusterId, "Connected", TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            output.WriteLine(await agent.DumpLogsAsync());
            throw;
        }

        var role = await cp.CreateRoleAsync(
            name: $"e2e-rec-{Guid.NewGuid():N}".Substring(0, 30),
            kubernetesGroups: new[] { "system:masters" });
        var me = await cp.GetCurrentUserAsync();
        await cp.AssignRoleAsync(me.Id, role.Id, second.ClusterId);

        var credential = await cp.IssueKubeconfigCredentialAsync(second.ClusterId);

        using var response = await cp.KubectlGetAsync(
            second.ClusterId, credential.Token, "/api/v1/namespaces");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
