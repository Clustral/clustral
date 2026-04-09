using System.Net;
using Clustral.E2E.Tests.Fixtures;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.E2E.Tests.Tests;

/// <summary>
/// Validates the Go agent's <c>RenewalManager</c> end-to-end:
///
///   • The agent's renewal loop wakes every <c>AGENT_RENEWAL_CHECK_INTERVAL</c>.
///   • If a credential's remaining lifetime is below the renewal threshold,
///     the agent calls <c>ClusterService.RenewCertificate</c> /
///     <c>ClusterService.RenewToken</c> over its existing mTLS+JWT channel.
///   • Newly rotated credentials must continue to authenticate the tunnel —
///     a regression here breaks long-lived agents in production.
///
/// We force renewal in seconds (instead of weeks) by giving the agent absurdly
/// large renewal thresholds — every check tick is "expires soon".
/// </summary>
[Collection(E2ETestCollection.Name)]
[Trait("Category", "E2E")]
public sealed class AgentRenewalTests(E2EFixture fixture, ITestOutputHelper output)
{
    /// <summary>
    /// Aggressive renewal: check every 2 seconds, treat anything expiring in
    /// less than a year as "expires soon" → forces both cert and JWT renewal
    /// on the very first tick after agent startup.
    /// </summary>
    private static readonly AgentRuntimeOptions AggressiveRenewal = new(
        RenewalCheckInterval: "2s",
        CertRenewThreshold: "8760h",
        JwtRenewThreshold: "8760h");

    [Fact]
    public async Task JwtRenewal_AgentLogsRenewalAndProxyKeepsWorking()
    {
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output,
            k8sGroups: new[] { "system:masters" },
            agentOptions: AggressiveRenewal);

        // The Go agent emits this exact line on successful JWT renewal —
        // see src/clustral-agent/internal/auth/renewal.go:143
        var found = await AgentLogReader.WaitForLogLineAsync(
            ctx.Agent.Container, "JWT renewed successfully", TimeSpan.FromSeconds(20));

        if (!found)
            output.WriteLine(await ctx.Agent.DumpLogsAsync());

        found.Should().BeTrue("the agent should renew its JWT within the first renewal tick");

        // Tunnel must still be alive after rotation.
        var cluster = await ctx.Cp.GetClusterAsync(ctx.ClusterId);
        cluster.Status.Should().Be("Connected");

        // And the proxy must still work.
        using var response = await ctx.Cp.KubectlGetAsync(
            ctx.ClusterId, ctx.CredentialToken, "/api/v1/namespaces");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CertRenewal_AgentLogsRenewalAndProxyKeepsWorking()
    {
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output,
            k8sGroups: new[] { "system:masters" },
            agentOptions: AggressiveRenewal);

        // src/clustral-agent/internal/auth/renewal.go:118
        var found = await AgentLogReader.WaitForLogLineAsync(
            ctx.Agent.Container, "Certificate renewed successfully", TimeSpan.FromSeconds(20));

        if (!found)
            output.WriteLine(await ctx.Agent.DumpLogsAsync());

        found.Should().BeTrue("the agent should renew its mTLS cert within the first renewal tick");

        var cluster = await ctx.Cp.GetClusterAsync(ctx.ClusterId);
        cluster.Status.Should().Be("Connected");

        using var response = await ctx.Cp.KubectlGetAsync(
            ctx.ClusterId, ctx.CredentialToken, "/api/v1/namespaces");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
