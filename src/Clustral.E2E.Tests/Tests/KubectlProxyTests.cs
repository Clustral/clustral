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
}
