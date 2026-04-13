using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clustral.E2E.Tests.Fixtures;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.E2E.Tests.Tests;

/// <summary>
/// The flagship E2E scenario: a real kubectl-style request enters the
/// ControlPlane proxy endpoint, traverses the gRPC tunnel into the Go agent,
/// and is forwarded to a real K3s API server. The response that comes back
/// must contain real Kubernetes objects.
///
/// This test exists specifically to catch the .NET HttpClient header
/// concatenation bug that motivated the Go agent rewrite. Any regression
/// where the agent merges multi-value impersonation headers will fail RBAC
/// against the K3s API.
/// </summary>
[Collection(E2ETestCollection.Name)]
[Trait("Category", "E2E")]
public sealed class KubectlProxyTests(E2EFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task ListNamespaces_ReturnsRealK3sNamespaces()
    {
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output, k8sGroups: new[] { "system:masters" });

        using var response = await ctx.Cp.KubectlGetAsync(
            ctx.ClusterId, ctx.CredentialToken, "/api/v1/namespaces");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"K3s response: {body[..Math.Min(body.Length, 500)]}");

        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items");
        var names = items.EnumerateArray()
            .Select(item => item.GetProperty("metadata").GetProperty("name").GetString())
            .ToList();

        names.Should().Contain("default", "default namespace exists in every k8s cluster");
        names.Should().Contain("kube-system");
    }

    [Fact]
    public async Task GetPods_KubeSystem_ReturnsK3sSystemPods()
    {
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output, k8sGroups: new[] { "system:masters" });

        using var response = await ctx.Cp.KubectlGetAsync(
            ctx.ClusterId, ctx.CredentialToken, "/api/v1/namespaces/kube-system/pods");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0,
            "k3s ships with several system pods (coredns, traefik, metrics-server, etc.)");
    }

    [Fact]
    public async Task CreateAndDeleteNamespace_RoundTripsRealK3sObject()
    {
        await using var ctx = await E2ETestContext.SetupAsync(
            fixture, output, k8sGroups: new[] { "system:masters" });

        var namespaceName = $"e2e-{Guid.NewGuid():N}".Substring(0, 30);
        var namespaceJson = JsonContent.Create(new
        {
            apiVersion = "v1",
            kind = "Namespace",
            metadata = new { name = namespaceName },
        });

        // Create
        using (var create = await ctx.Cp.KubectlSendAsync(
            HttpMethod.Post, ctx.ClusterId, ctx.CredentialToken,
            "/api/v1/namespaces", namespaceJson))
        {
            create.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Read back
        using (var get = await ctx.Cp.KubectlGetAsync(
            ctx.ClusterId, ctx.CredentialToken, $"/api/v1/namespaces/{namespaceName}"))
        {
            get.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Delete
        using (var delete = await ctx.Cp.KubectlSendAsync(
            HttpMethod.Delete, ctx.ClusterId, ctx.CredentialToken,
            $"/api/v1/namespaces/{namespaceName}"))
        {
            delete.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task ProxyWithInvalidClusterUuid_AndValidToken_Returns400()
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        // Register any cluster + issue a real kubeconfig credential so we have
        // an authentic token that passes gateway JWT validation. The gateway
        // (defense in depth) rejects bad/missing tokens with 401 before any
        // request body or path validation — so we must authenticate first to
        // exercise the URL-validation 400 path on the ControlPlane side.
        var registration = await cp.RegisterClusterAsync(
            $"e2e-baduuid-{Guid.NewGuid():N}".Substring(0, 30));
        var credential = await cp.IssueKubeconfigCredentialAsync(registration.ClusterId);

        using var http = new HttpClient { BaseAddress = cp.BaseAddress };
        var request = new HttpRequestMessage(HttpMethod.Get, "api/proxy/not-a-uuid/api/v1/namespaces");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", credential.Token);

        using var response = await http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "with a valid token, the ControlPlane proxy middleware rejects malformed cluster IDs");
    }

    [Fact]
    public async Task ProxyWithoutBearerToken_Returns401()
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        var registration = await cp.RegisterClusterAsync($"e2e-noauth-{Guid.NewGuid():N}".Substring(0, 30));

        using var http = new HttpClient { BaseAddress = cp.BaseAddress };
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/proxy/{registration.ClusterId}/api/v1/namespaces");
        // Intentionally no Authorization header.

        using var response = await http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "missing bearer token is rejected at the proxy auth layer");
    }

    [Fact]
    public async Task ProxyWhenAgentDisconnected_Returns502()
    {
        var cp = fixture.CreateControlPlaneClient();
        await cp.SignInAsync();

        // Register a cluster but never start an agent.
        var registration = await cp.RegisterClusterAsync($"e2e-noagent-{Guid.NewGuid():N}".Substring(0, 30));

        // Assign a role so we get past the role check (otherwise we'd see 403, not 502).
        var role = await cp.CreateRoleAsync(
            name: $"e2e-noagent-role-{Guid.NewGuid():N}".Substring(0, 30),
            kubernetesGroups: new[] { "system:masters" });
        var me = await cp.GetCurrentUserAsync();
        await cp.AssignRoleAsync(me.Id, role.Id, registration.ClusterId);

        // Issue credential.
        var credential = await cp.IssueKubeconfigCredentialAsync(registration.ClusterId);

        // Proxy request without an active agent → BadGateway (no tunnel session).
        using var response = await cp.KubectlGetAsync(
            registration.ClusterId, credential.Token, "/api/v1/namespaces");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway,
            "no tunnel session for the cluster — agent never connected");
    }
}
