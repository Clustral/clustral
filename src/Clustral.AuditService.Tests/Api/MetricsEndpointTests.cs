using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Clustral.AuditService.Tests.Api;

/// <summary>
/// Integration test verifying the Prometheus /metrics endpoint returns
/// valid metrics in text/plain format.
/// </summary>
[Collection("Mongo")]
public sealed class MetricsEndpointTests(MongoFixture mongo, ITestOutputHelper output)
    : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["MongoDB:ConnectionString"] = mongo.ConnectionString,
                        ["MongoDB:DatabaseName"] = $"test-metrics-{Guid.NewGuid():N}",
                        ["RabbitMQ:Host"] = "localhost",
                        ["OpenTelemetry:ServiceName"] = "test-audit",
                        ["OpenTelemetry:OtlpEndpoint"] = "http://localhost:4317",
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMongoClient>();
                    services.AddSingleton<IMongoClient>(
                        _ => new MongoClient(mongo.ConnectionString));
                });
            });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusFormat()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/metrics");

        output.WriteLine($"Status: {response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Body (first 500 chars): {body[..Math.Min(500, body.Length)]}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType
            .Should().Contain("text/plain");

        // Prometheus format: lines starting with # are comments/metadata,
        // metric lines are "metric_name{labels} value"
        body.Should().Contain("# HELP");
        body.Should().Contain("# TYPE");
    }
}
