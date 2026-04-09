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
}
