using Xunit.Abstractions;

namespace Clustral.E2E.Tests.Fixtures;

/// <summary>
/// One-call test setup that registers a cluster, starts an agent, optionally
/// creates and assigns a role, and issues a kubeconfig credential. Disposing
/// the context tears down the per-test agent container.
///
/// Use this when you need a "ready to send kubectl" environment without
/// repeating the same boilerplate in every test.
/// </summary>
public sealed class E2ETestContext : IAsyncDisposable
{
    public required ControlPlaneClient Cp { get; init; }
    public required AgentHandle Agent { get; init; }
    public required Guid ClusterId { get; init; }
    public required string CredentialToken { get; init; }
    public required Guid CredentialId { get; init; }
    public Guid? RoleId { get; init; }

    public static async Task<E2ETestContext> SetupAsync(
        E2EFixture fixture,
        ITestOutputHelper output,
        IReadOnlyList<string>? k8sGroups = null,
        AgentRuntimeOptions? agentOptions = null,
        CancellationToken ct = default)
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync(ct: ct);

        var registration = await cp.RegisterClusterAsync(
            $"e2e-{Guid.NewGuid():N}".Substring(0, 30), ct: ct);
        output.WriteLine($"Registered cluster {registration.ClusterId}");

        AgentHandle? agent = null;
        try
        {
            agent = await fixture.StartAgentAsync(
                registration.ClusterId, registration.BootstrapToken, agentOptions, ct);

            // Wait for the tunnel to come up before issuing credentials.
            try
            {
                await cp.WaitForClusterStatusAsync(
                    registration.ClusterId, "Connected", TimeSpan.FromSeconds(60), ct);
            }
            catch (TimeoutException)
            {
                output.WriteLine(await agent.DumpLogsAsync(ct));
                throw;
            }

            // Optional role assignment for the current user.
            Guid? roleId = null;
            if (k8sGroups is { Count: > 0 })
            {
                var role = await cp.CreateRoleAsync(
                    name: $"e2e-role-{Guid.NewGuid():N}".Substring(0, 30),
                    kubernetesGroups: k8sGroups,
                    ct: ct);
                roleId = role.Id;

                var me = await cp.GetCurrentUserAsync(ct);
                await cp.AssignRoleAsync(me.Id, role.Id, registration.ClusterId, ct);
            }

            // Issue a short-lived kubeconfig credential the test can use as a Bearer token.
            var credential = await cp.IssueKubeconfigCredentialAsync(registration.ClusterId, ct: ct);

            return new E2ETestContext
            {
                Cp = cp,
                Agent = agent,
                ClusterId = registration.ClusterId,
                CredentialToken = credential.Token,
                CredentialId = credential.CredentialId,
                RoleId = roleId,
            };
        }
        catch
        {
            if (agent is not null) await agent.DisposeAsync();
            cp.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Agent.DisposeAsync();
        Cp.Dispose();
    }
}
