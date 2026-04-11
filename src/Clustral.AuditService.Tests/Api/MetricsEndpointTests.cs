using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit.Abstractions;

namespace Clustral.AuditService.Tests.Api;

/// <summary>
/// Integration test verifying the Prometheus /metrics endpoint returns
/// valid metrics in text/plain format.
/// </summary>
public sealed class MetricsEndpointTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder()
        .WithImage("mongo:8")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IMongoClient>();
                    services.AddSingleton<IMongoClient>(
                        _ => new MongoClient(_mongo.GetConnectionString()));
                });
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["MongoDB:ConnectionString"] = _mongo.GetConnectionString(),
                        ["MongoDB:DatabaseName"] = $"test-metrics-{Guid.NewGuid():N}",
                        ["RabbitMQ:Host"] = "localhost",
                        ["OpenTelemetry:ServiceName"] = "test-audit",
                        ["OpenTelemetry:OtlpEndpoint"] = "http://localhost:4317",
                    });
                });
            });
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _mongo.DisposeAsync();
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
