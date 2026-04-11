using Clustral.AuditService.Consumers;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Clustral.AuditService.Tests.Consumers;

[Collection("Mongo")]
public sealed class ProxyConsumerTests(MongoFixture mongo, ITestOutputHelper output)
{
    [Fact]
    public async Task SuccessfulRequest_PersistsAuditEvent_WithInfoSeverity()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var clusterId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();

        await harness.Bus.Publish(new ProxyRequestCompletedEvent
        {
            ClusterId = clusterId,
            ClusterName = "prod",
            UserId = userId,
            UserEmail = "alice@example.com",
            CredentialId = credentialId,
            Method = "GET",
            Path = "/api/v1/pods",
            StatusCode = 200,
            DurationMs = 42.5,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<ProxyRequestCompletedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event: {stored?.Message}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.ProxyRequestCompleted);
        stored.Event.Should().Be("proxy.request");
        stored.Category.Should().Be("proxy");
        stored.Severity.Should().Be(Severity.Info);
        stored.Success.Should().BeTrue();
        stored.User.Should().Be("alice@example.com");
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("prod");
        stored.ResourceType.Should().Be("Credential");
        stored.ResourceId.Should().Be(credentialId);
        stored.Message.Should().Contain("GET").And.Contain("/api/v1/pods").And.Contain("200");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task ErrorStatusCode_SetsWarningSeverity_AndSuccessFalse(int statusCode)
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ProxyRequestCompletedEvent
        {
            ClusterId = Guid.NewGuid(),
            ClusterName = "staging",
            UserId = Guid.NewGuid(),
            UserEmail = "bob@example.com",
            CredentialId = Guid.NewGuid(),
            Method = "DELETE",
            Path = "/api/v1/namespaces/default",
            StatusCode = statusCode,
            DurationMs = 150.0,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<ProxyRequestCompletedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Status {statusCode} -> severity={stored?.Severity}, success={stored?.Success}");

        stored.Should().NotBeNull();
        stored!.Severity.Should().Be(Severity.Warning);
        stored.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(399)]
    public async Task SuccessStatusCode_SetsInfoSeverity_AndSuccessTrue(int statusCode)
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ProxyRequestCompletedEvent
        {
            ClusterId = Guid.NewGuid(),
            ClusterName = "dev",
            UserId = Guid.NewGuid(),
            UserEmail = "dev@example.com",
            CredentialId = Guid.NewGuid(),
            Method = "GET",
            Path = "/healthz",
            StatusCode = statusCode,
            DurationMs = 5.0,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<ProxyRequestCompletedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Status {statusCode} -> severity={stored?.Severity}, success={stored?.Success}");

        stored.Should().NotBeNull();
        stored!.Severity.Should().Be(Severity.Info);
        stored.Success.Should().BeTrue();
    }

    private static ServiceProvider BuildProvider(AuditDbContext db)
    {
        return new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IAuditEventRepository, MongoAuditEventRepository>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<ProxyRequestCompletedConsumer>())
            .BuildServiceProvider(true);
    }
}
