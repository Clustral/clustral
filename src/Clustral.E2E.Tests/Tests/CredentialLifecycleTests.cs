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
}
