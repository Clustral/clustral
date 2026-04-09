using System.Net;
using System.Net.Http.Json;
using Clustral.E2E.Tests.Fixtures;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.E2E.Tests.Tests;

/// <summary>
/// Verifies that role-based access control is enforced end-to-end:
///   1. The ControlPlane refuses requests when no role is assigned.
///   2. The Go agent forwards multi-value Impersonate-Group headers correctly,
///      so K3s' real RBAC sees the right groups.
///   3. K3s system roles (system:masters, view, etc.) gate writes as expected.
///
/// The bug this catches: if the agent merges multi-value impersonation headers
/// with commas (the .NET HttpClient bug), K3s sees one bogus group instead of
/// many real ones — RBAC silently misbehaves.
/// </summary>
[Collection(E2ETestCollection.Name)]
[Trait("Category", "E2E")]
public sealed class RoleBasedAccessTests(E2EFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task NoRoleAssigned_ProxyReturns403()
    {
        // No k8sGroups → no role created → no assignment.
        await using var ctx = await E2ETestContext.SetupAsync(fixture, output, k8sGroups: null);

        using var response = await ctx.Cp.KubectlGetAsync(
            ctx.ClusterId, ctx.CredentialToken, "/api/v1/namespaces");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the ControlPlane proxy denies requests for users with no role assignment for the cluster");
    }

    [Fact]
    public async Task SystemMasters_CanCreateNamespaces()
    {
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output, k8sGroups: new[] { "system:masters" });

        var name = $"e2e-rbac-{Guid.NewGuid():N}".Substring(0, 30);
        using var response = await ctx.Cp.KubectlSendAsync(
            HttpMethod.Post, ctx.ClusterId, ctx.CredentialToken,
            "/api/v1/namespaces",
            JsonContent.Create(new
            {
                apiVersion = "v1",
                kind = "Namespace",
                metadata = new { name },
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Cleanup
        using var _ = await ctx.Cp.KubectlSendAsync(
            HttpMethod.Delete, ctx.ClusterId, ctx.CredentialToken,
            $"/api/v1/namespaces/{name}");
    }

    [Fact]
    public async Task UnknownGroup_K3sRbacDeniesWrite()
    {
        // Assign a role with a group that has no ClusterRoleBinding in K3s.
        // K3s should accept the impersonation but deny the write.
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output, k8sGroups: new[] { "e2e-no-such-group" });

        var name = $"e2e-rbac-deny-{Guid.NewGuid():N}".Substring(0, 30);
        using var response = await ctx.Cp.KubectlSendAsync(
            HttpMethod.Post, ctx.ClusterId, ctx.CredentialToken,
            "/api/v1/namespaces",
            JsonContent.Create(new
            {
                apiVersion = "v1",
                kind = "Namespace",
                metadata = new { name },
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "K3s RBAC denies writes for groups without a matching ClusterRoleBinding");
    }

    [Fact]
    public async Task StaticAssignment_ProxyWorksWithoutJitRequest()
    {
        // E2ETestContext.SetupAsync uses static assignment (AssignRoleAsync),
        // not the JIT access request flow. This test makes that explicit.
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output, k8sGroups: new[] { "system:masters" });

        using var response = await ctx.Cp.KubectlGetAsync(
            ctx.ClusterId, ctx.CredentialToken, "/api/v1/namespaces");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "ImpersonationResolver checks static assignments before JIT grants");
    }

    [Fact]
    public async Task StaticAssignmentRemoved_ProxyReturns403()
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        var registration = await cp.RegisterClusterAsync($"e2e-rm-{Guid.NewGuid():N}".Substring(0, 30));
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
            name: $"e2e-rm-role-{Guid.NewGuid():N}".Substring(0, 30),
            kubernetesGroups: new[] { "system:masters" });
        var me = await cp.GetCurrentUserAsync();
        var assignment = await cp.AssignRoleAsync(me.Id, role.Id, registration.ClusterId);

        // Issue credential while assignment exists — proxy should work.
        var credentialBefore = await cp.IssueKubeconfigCredentialAsync(registration.ClusterId);
        using (var ok = await cp.KubectlGetAsync(registration.ClusterId, credentialBefore.Token, "/api/v1/namespaces"))
        {
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Remove the assignment.
        await cp.RemoveAssignmentAsync(me.Id, assignment.Id);

        // Issue a NEW credential — should still succeed (issuing doesn't depend on the role)
        // but proxy should fail (no static assignment, no JIT grant).
        var credentialAfter = await cp.IssueKubeconfigCredentialAsync(registration.ClusterId);
        using var rejected = await cp.KubectlGetAsync(registration.ClusterId, credentialAfter.Token, "/api/v1/namespaces");
        rejected.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "removing the static assignment must immediately deny proxy access");
    }

    [Fact]
    public async Task RoleWithMultipleGroups_AllForwardedAsSeparateHeaders()
    {
        // Regression test for the .NET HttpClient multi-value header bug.
        // The role has system:masters (so K3s accepts the request) plus two
        // additional custom groups. The Go agent must forward all three as
        // separate Impersonate-Group headers; if it merges them with commas,
        // K3s' impersonation handling rejects the request.
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output, k8sGroups: new[]
            {
                "system:masters",
                "e2e-extra-group-1",
                "e2e-extra-group-2",
            });

        var name = $"e2e-multi-{Guid.NewGuid():N}".Substring(0, 30);
        using var response = await ctx.Cp.KubectlSendAsync(
            HttpMethod.Post, ctx.ClusterId, ctx.CredentialToken,
            "/api/v1/namespaces",
            JsonContent.Create(new
            {
                apiVersion = "v1",
                kind = "Namespace",
                metadata = new { name },
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "K3s accepts the request because system:masters is one of the impersonated groups; " +
            "if multi-value headers are merged with commas, none of them parse correctly and K3s denies");

        // Cleanup
        using var _ = await ctx.Cp.KubectlSendAsync(
            HttpMethod.Delete, ctx.ClusterId, ctx.CredentialToken,
            $"/api/v1/namespaces/{name}");
    }
}
