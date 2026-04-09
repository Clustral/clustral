using System.Net;
using Clustral.E2E.Tests.Fixtures;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.E2E.Tests.Tests;

/// <summary>
/// Verifies the kubeconfig credential lifecycle through the proxy:
/// issue, use, revoke, and confirm that subsequent requests are rejected.
/// </summary>
[Collection(E2ETestCollection.Name)]
[Trait("Category", "E2E")]
public sealed class CredentialLifecycleTests(E2EFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task IssueUseRevoke_RevokedTokenRejectedAt401()
    {
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output, k8sGroups: new[] { "system:masters" });

        // Use the freshly issued credential — should succeed.
        using (var first = await ctx.Cp.KubectlGetAsync(
            ctx.ClusterId, ctx.CredentialToken, "/api/v1/namespaces"))
        {
            first.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Revoke
        await ctx.Cp.RevokeCredentialAsync(ctx.CredentialId);

        // The same token should no longer work.
        using var second = await ctx.Cp.KubectlGetAsync(
            ctx.ClusterId, ctx.CredentialToken, "/api/v1/namespaces");
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "ProxyAuthService rejects revoked credentials");
    }

    [Fact]
    public async Task ExpiredCredential_ProxyReturns401()
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        var registration = await cp.RegisterClusterAsync($"e2e-exp-{Guid.NewGuid():N}".Substring(0, 30));
        await using var agent = await fixture.StartAgentAsync(registration.ClusterId, registration.BootstrapToken);

        try
        {
            await cp.WaitForClusterStatusAsync(registration.ClusterId, "Connected", TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            output.WriteLine(await agent.DumpLogsAsync());
            throw;
        }

        // Create role + assign so the user can actually proxy while the token is valid.
        var role = await cp.CreateRoleAsync(
            name: $"e2e-exp-role-{Guid.NewGuid():N}".Substring(0, 30),
            kubernetesGroups: new[] { "system:masters" });
        var me = await cp.GetCurrentUserAsync();
        await cp.AssignRoleAsync(me.Id, role.Id, registration.ClusterId);

        // Issue a credential with a 2-second TTL.
        var credential = await cp.IssueKubeconfigCredentialAsync(
            registration.ClusterId, requestedTtl: "00:00:02");

        // Proxy works while valid.
        using (var ok = await cp.KubectlGetAsync(registration.ClusterId, credential.Token, "/api/v1/namespaces"))
        {
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Wait for expiry.
        await Task.Delay(TimeSpan.FromSeconds(4));

        // Should be rejected at the proxy auth layer.
        using var expired = await cp.KubectlGetAsync(registration.ClusterId, credential.Token, "/api/v1/namespaces");
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "ProxyAuthService rejects expired credentials before tunnel forwarding");
    }

    [Fact]
    public async Task CredentialForClusterA_UsedOnClusterB_Returns403()
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        // Cluster A — register, start agent, assign role, issue credential.
        var regA = await cp.RegisterClusterAsync($"e2e-a-{Guid.NewGuid():N}".Substring(0, 30));
        await using var agentA = await fixture.StartAgentAsync(regA.ClusterId, regA.BootstrapToken);

        // Cluster B — register, start agent.
        var regB = await cp.RegisterClusterAsync($"e2e-b-{Guid.NewGuid():N}".Substring(0, 30));
        await using var agentB = await fixture.StartAgentAsync(regB.ClusterId, regB.BootstrapToken);

        try
        {
            await cp.WaitForClusterStatusAsync(regA.ClusterId, "Connected", TimeSpan.FromSeconds(60));
            await cp.WaitForClusterStatusAsync(regB.ClusterId, "Connected", TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            output.WriteLine("=== Agent A logs ===");
            output.WriteLine(await agentA.DumpLogsAsync());
            output.WriteLine("=== Agent B logs ===");
            output.WriteLine(await agentB.DumpLogsAsync());
            throw;
        }

        var role = await cp.CreateRoleAsync(
            name: $"e2e-cross-role-{Guid.NewGuid():N}".Substring(0, 30),
            kubernetesGroups: new[] { "system:masters" });
        var me = await cp.GetCurrentUserAsync();
        // Assign to cluster A only.
        await cp.AssignRoleAsync(me.Id, role.Id, regA.ClusterId);

        // Credential issued for A.
        var credential = await cp.IssueKubeconfigCredentialAsync(regA.ClusterId);

        // Works on A.
        using (var onA = await cp.KubectlGetAsync(regA.ClusterId, credential.Token, "/api/v1/namespaces"))
        {
            onA.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Same token used on B → must be rejected (credential is scoped to A).
        using var onB = await cp.KubectlGetAsync(regB.ClusterId, credential.Token, "/api/v1/namespaces");
        onB.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized },
            "credential scoped to cluster A must not work on cluster B");
    }
}
