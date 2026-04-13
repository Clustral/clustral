using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

/// <summary>
/// Verifies the path-aware error-body split in action. Requests to
/// <c>/api/proxy/*</c> return Kubernetes <c>v1.Status</c> JSON; requests to
/// the REST API (<c>/api/v1/*</c>) keep returning RFC 7807 Problem Details.
///
/// See the "Error Response Shapes" section in the root README for the full
/// rationale behind the split.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProxyErrorShapeTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    // ── v1.Status shape on /api/proxy/* ───────────────────────────────────

    [Fact]
    public async Task Proxy_MalformedClusterId_ReturnsV1Status_InvalidClusterId()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/proxy/not-a-uuid/api/v1/namespaces");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine(body);

        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("kind").GetString().Should().Be("Status");
        json.GetProperty("apiVersion").GetString().Should().Be("v1");
        json.GetProperty("status").GetString().Should().Be("Failure");
        json.GetProperty("reason").GetString().Should().Be("BadRequest");
        json.GetProperty("code").GetInt32().Should().Be(400);
        json.GetProperty("details").GetProperty("causes")[0]
            .GetProperty("reason").GetString().Should().Be("INVALID_CLUSTER_ID");

        // Correlation ID echoed on the response header.
        response.Headers.TryGetValues("X-Correlation-Id", out var ids).Should().BeTrue();
        ids!.First().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Proxy_MessageIsSingleLine_KubectlRenderable()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/proxy/not-a-uuid/api/v1/namespaces");
        var body = await response.Content.ReadAsStringAsync();
        var message = JsonDocument.Parse(body).RootElement.GetProperty("message").GetString();

        output.WriteLine($"kubectl would render: error: {message}");
        // kubectl prints `status.message` verbatim, prefixed with "error: ".
        // The message must stay a clean single line.
        message.Should().NotContain("\n");
        message.Should().NotBeNullOrWhiteSpace();
    }

    // ── RFC 7807 shape on non-proxy paths ─────────────────────────────────

    [Fact]
    public async Task RestApi_NotFound_ReturnsProblemDetails()
    {
        var client = factory.CreateAuthenticatedClient();
        var missing = Guid.NewGuid();
        var response = await client.GetAsync($"/api/v1/clusters/{missing}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Proves the path-aware split: REST endpoints stay on RFC 7807 even
        // though the proxy path emits v1.Status.
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine(body);

        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("type").GetString().Should().StartWith("urn:clustral:error:");
        json.GetProperty("status").GetInt32().Should().Be(404);
        json.GetProperty("code").GetString().Should().Be("CLUSTER_NOT_FOUND");
    }

    // ── Correlation ID propagation ────────────────────────────────────────

    [Fact]
    public async Task Proxy_EchoesIncomingCorrelationId_UnchangedInResponse()
    {
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "/api/proxy/not-a-uuid/api/v1/namespaces");
        req.Headers.Add("X-Correlation-Id", "caller-provided-id-xyz");

        var response = await client.SendAsync(req);
        response.Headers.GetValues("X-Correlation-Id").First()
            .Should().Be("caller-provided-id-xyz");
    }

    [Fact]
    public async Task RestApi_EchoesIncomingCorrelationId_UnchangedInResponse()
    {
        using var client = factory.CreateAuthenticatedClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/clusters/{Guid.NewGuid()}");
        req.Headers.Add("X-Correlation-Id", "rest-caller-id-abc");

        var response = await client.SendAsync(req);
        response.Headers.GetValues("X-Correlation-Id").First()
            .Should().Be("rest-caller-id-abc");
    }
}
