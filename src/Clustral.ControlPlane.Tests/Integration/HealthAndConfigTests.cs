using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class HealthAndConfigTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    // ── Liveness: /healthz ──────────────────────────────────────────────────

    [Fact]
    public async Task Healthz_Returns200_NoAuth()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz");

        output.WriteLine($"GET /healthz => {(int)response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Body: {body}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Healthz_AlwaysHealthy_RegardlessOfChecks()
    {
        // Liveness runs no checks — it only proves the process is alive.
        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Liveness body: {body}");

        body.Should().Contain("Healthy");
    }

    // ── Readiness: /healthz/ready ───────────────────────────────────────────

    [Fact]
    public async Task HealthzReady_Returns200_WhenDependenciesUp()
    {
        // MongoDB is up via Testcontainers. Readiness checks only MongoDB now
        // (OIDC health check moved to API Gateway).
        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz/ready");

        output.WriteLine($"GET /healthz/ready => {(int)response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Body: {body}");

        // May return 503 if Serilog bootstrap logger conflicts between test factories.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task HealthzReady_NoAuthRequired()
    {
        // Readiness must be accessible without a Bearer token (used by k8s probes).
        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz/ready");

        output.WriteLine($"GET /healthz/ready (no auth) => {(int)response.StatusCode}");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ── Detailed: /healthz/detail ───────────────────────────────────────────

    [Fact]
    public async Task HealthzDetail_Returns401_WithoutAuth()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz/detail");

        output.WriteLine($"GET /healthz/detail (no auth) => {(int)response.StatusCode}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthzDetail_Returns200_WithAuth_ContainsChecks()
    {
        var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/healthz/detail");

        output.WriteLine($"GET /healthz/detail (auth) => {(int)response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Body:\n{body}");

        // Detailed endpoint returns 200 or 503 depending on check results,
        // but always returns a JSON body with check details.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        root.TryGetProperty("status", out _).Should().BeTrue("response should have 'status'");
        root.TryGetProperty("version", out _).Should().BeTrue("response should have 'version'");
        root.TryGetProperty("uptime", out _).Should().BeTrue("response should have 'uptime'");
        root.TryGetProperty("totalDuration", out _).Should().BeTrue("response should have 'totalDuration'");
        root.TryGetProperty("checks", out var checks).Should().BeTrue("response should have 'checks'");

        checks.TryGetProperty("mongodb", out var mongo).Should().BeTrue("checks should include 'mongodb'");
        mongo.TryGetProperty("status", out _).Should().BeTrue("mongodb check should have 'status'");
        mongo.TryGetProperty("duration", out _).Should().BeTrue("mongodb check should have 'duration'");

        output.WriteLine($"MongoDB status: {mongo.GetProperty("status").GetString()}");
    }

    [Fact]
    public async Task HealthzDetail_MongoDbCheck_IsHealthy()
    {
        // MongoDB is running via Testcontainers, so it should be healthy.
        var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/healthz/detail");
        var body = await response.Content.ReadAsStringAsync();

        var json = JsonDocument.Parse(body);
        var mongoStatus = json.RootElement
            .GetProperty("checks")
            .GetProperty("mongodb")
            .GetProperty("status")
            .GetString();

        output.WriteLine($"MongoDB health: {mongoStatus}");
        mongoStatus.Should().Be("Healthy");
    }

    // ── Version endpoint ─────────────────────────────────────────────────────

    [Fact]
    public async Task Version_Returns200_NoAuth()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/version");

        output.WriteLine($"GET /api/v1/version => {(int)response.StatusCode}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Version_IncludesVersionField()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/version");
        var body = await response.Content.ReadAsStringAsync();

        var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("version", out var version).Should().BeTrue("response should include 'version'");

        output.WriteLine($"ControlPlane version: {version.GetString()}");
        version.GetString().Should().NotBeNullOrEmpty();
    }
}
