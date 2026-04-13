using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

/// <summary>
/// Verifies the path-aware error-body split. Requests to <c>/api/proxy/*</c>
/// return plain text (so kubectl's client-go discovery path renders the
/// message verbatim); requests to the REST API (<c>/api/v1/*</c>) keep
/// returning RFC 7807 Problem Details.
///
/// See the "Error Response Shapes" section in the root README for the full
/// rationale behind the split.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProxyErrorShapeTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    // ── Plain text on /api/proxy/* ────────────────────────────────────────

    [Fact]
    public async Task Proxy_MalformedClusterId_ReturnsPlainTextWithErrorCodeHeader()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/proxy/not-a-uuid/api/v1/namespaces");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine(body);

        // The message is self-speaking — names what went wrong, which
        // field, and how to fix it.
        body.Should().Contain("not-a-uuid")
            .And.Contain("not a valid UUID")
            .And.Contain("clustral kube login");

        // Machine-readable code lives in the X-Clustral-Error-Code header.
        response.Headers.TryGetValues("X-Clustral-Error-Code", out var codes).Should().BeTrue();
        codes!.First().Should().Be("INVALID_CLUSTER_ID");

        // Correlation ID echoed on every response.
        response.Headers.TryGetValues("X-Correlation-Id", out var ids).Should().BeTrue();
        ids!.First().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Proxy_MessageIsSingleLine_KubectlRenderable()
    {
        // kubectl prints the body verbatim after "error: " — no embedded
        // newlines so the terminal output stays clean.
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/proxy/not-a-uuid/api/v1/namespaces");
        var message = await response.Content.ReadAsStringAsync();

        output.WriteLine($"kubectl would render: error: {message}");
        message.Should().NotContain("\n");
        message.Should().NotContain("\r");
        message.Should().NotBeNullOrWhiteSpace();
    }

    // ── RFC 7807 on REST API paths ────────────────────────────────────────

    [Fact]
    public async Task RestApi_NotFound_ReturnsProblemDetails()
    {
        var client = factory.CreateAuthenticatedClient();
        var missing = Guid.NewGuid();
        var response = await client.GetAsync($"/api/v1/clusters/{missing}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Proves the path-aware split: REST endpoints stay on RFC 7807 even
        // though the proxy path emits plain text.
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine(body);

        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("type").GetString().Should().StartWith("https://docs.clustral.kube.it.com/errors/");
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
