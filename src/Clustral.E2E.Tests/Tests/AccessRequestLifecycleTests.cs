using System.Net;
using Clustral.E2E.Tests.Fixtures;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.E2E.Tests.Tests;

/// <summary>
/// End-to-end tests for the JIT access request lifecycle:
///   request → approve/deny → proxy works/denied → grant expires/revoked.
///
/// These exercise the credential TTL capping in IssueKubeconfigCredential
/// (which caps to grant expiry) and the runtime check in
/// ImpersonationResolver.GetActiveGrantAsync.
/// </summary>
[Collection(E2ETestCollection.Name)]
[Trait("Category", "E2E")]
public sealed class AccessRequestLifecycleTests(E2EFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RequestApprove_ProxyWorks_ThenExpires_403()
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        // Register cluster + start agent (no role assignment yet).
        var registration = await cp.RegisterClusterAsync($"e2e-jit-{Guid.NewGuid():N}".Substring(0, 30));
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

        // Create a role with system:masters so the JIT grant maps to actual k8s permissions.
        var role = await cp.CreateRoleAsync(
            name: $"e2e-jit-role-{Guid.NewGuid():N}".Substring(0, 30),
            kubernetesGroups: new[] { "system:masters" });

        // Request JIT access with a 4-second duration so it expires fast.
        var request = await cp.CreateAccessRequestAsync(
            roleId: role.Id,
            clusterId: registration.ClusterId,
            reason: "e2e test",
            requestedDuration: "PT4S");

        request.Status.Should().Be("Pending");

        // Approve.
        var approved = await cp.ApproveAccessRequestAsync(request.Id);
        approved.Status.Should().Be("Approved");
        approved.GrantExpiresAt.Should().NotBeNull();

        // Issue credential — TTL should be capped to grant expiry (≤4s).
        var credential = await cp.IssueKubeconfigCredentialAsync(registration.ClusterId);

        // Proxy should work while grant is active.
        using (var ok = await cp.KubectlGetAsync(registration.ClusterId, credential.Token, "/api/v1/namespaces"))
        {
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Wait for the grant to expire.
        await Task.Delay(TimeSpan.FromSeconds(6));

        // After expiry: ProxyAuthService should reject (token expired with grant) → 401 or 403.
        using var expired = await cp.KubectlGetAsync(registration.ClusterId, credential.Token, "/api/v1/namespaces");
        expired.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequestDeny_ProxyReturns403()
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        var registration = await cp.RegisterClusterAsync($"e2e-deny-{Guid.NewGuid():N}".Substring(0, 30));
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

        var role = await cp.CreateRoleAsync(
            name: $"e2e-deny-role-{Guid.NewGuid():N}".Substring(0, 30),
            kubernetesGroups: new[] { "system:masters" });

        var request = await cp.CreateAccessRequestAsync(
            roleId: role.Id,
            clusterId: registration.ClusterId,
            reason: "should be denied",
            requestedDuration: "PT30M");

        // Deny.
        await cp.DenyAccessRequestAsync(request.Id, "test denial");

        // Issuing a credential succeeds (the user can still authenticate),
        // but the proxy should reject because there's no active grant or static assignment.
        var credential = await cp.IssueKubeconfigCredentialAsync(registration.ClusterId);

        using var response = await cp.KubectlGetAsync(registration.ClusterId, credential.Token, "/api/v1/namespaces");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "no active grant or static role assignment");
    }

    [Fact]
    public async Task RevokeActiveGrant_ProxyImmediatelyRejects_403()
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        var registration = await cp.RegisterClusterAsync($"e2e-revoke-{Guid.NewGuid():N}".Substring(0, 30));
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

        var role = await cp.CreateRoleAsync(
            name: $"e2e-revoke-role-{Guid.NewGuid():N}".Substring(0, 30),
            kubernetesGroups: new[] { "system:masters" });

        var request = await cp.CreateAccessRequestAsync(
            roleId: role.Id,
            clusterId: registration.ClusterId,
            reason: "to be revoked",
            requestedDuration: "PT30M");

        await cp.ApproveAccessRequestAsync(request.Id);

        var credential = await cp.IssueKubeconfigCredentialAsync(registration.ClusterId);

        // Proxy works.
        using (var ok = await cp.KubectlGetAsync(registration.ClusterId, credential.Token, "/api/v1/namespaces"))
        {
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Revoke the grant.
        await cp.RevokeAccessRequestAsync(request.Id, "test revoke");

        // Next proxy call should be rejected immediately (no caching of grant state).
        using var rejected = await cp.KubectlGetAsync(registration.ClusterId, credential.Token, "/api/v1/namespaces");
        rejected.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }
}
