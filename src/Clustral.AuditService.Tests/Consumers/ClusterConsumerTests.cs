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
public sealed class ClusterConsumerTests(MongoFixture mongo, ITestOutputHelper output)
{
    [Fact]
    public async Task RegisteredConsumer_PersistsAuditEvent_WithClusterName()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<ClusterRegisteredConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var clusterId = Guid.NewGuid();

        await harness.Bus.Publish(new ClusterRegisteredEvent
        {
            ClusterId = clusterId,
            Name = "production-east",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<ClusterRegisteredEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.ClusterRegistered);
        stored.Event.Should().Be("cluster.registered");
        stored.Category.Should().Be("clusters");
        stored.Severity.Should().Be(Severity.Info);
        stored.ResourceType.Should().Be("Cluster");
        stored.ResourceId.Should().Be(clusterId);
        stored.ResourceName.Should().Be("production-east");
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("production-east");
        stored.Message.Should().Contain("production-east");
    }

    [Fact]
    public async Task ConnectedConsumer_PersistsAuditEvent_WithK8sVersion()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<ClusterConnectedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var clusterId = Guid.NewGuid();

        await harness.Bus.Publish(new ClusterConnectedEvent
        {
            ClusterId = clusterId,
            ClusterName = "production-east",
            KubernetesVersion = "v1.30.2",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<ClusterConnectedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.ClusterConnected);
        stored.Event.Should().Be("cluster.connected");
        stored.Category.Should().Be("clusters");
        stored.Severity.Should().Be(Severity.Info);
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("production-east");
        stored.Message.Should().Contain("v1.30.2");
    }

    [Fact]
    public async Task DisconnectedConsumer_PersistsAuditEvent_WithWarningSeverity()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<ClusterDisconnectedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var clusterId = Guid.NewGuid();

        await harness.Bus.Publish(new ClusterDisconnectedEvent
        {
            ClusterId = clusterId,
            ClusterName = "production-west",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<ClusterDisconnectedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.ClusterDisconnected);
        stored.Event.Should().Be("cluster.disconnected");
        stored.Category.Should().Be("clusters");
        stored.Severity.Should().Be(Severity.Warning);
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("production-west");
        stored.Message.Should().Contain("disconnected");
    }

    [Fact]
    public async Task DeletedConsumer_PersistsAuditEvent()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<ClusterDeletedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var clusterId = Guid.NewGuid();

        await harness.Bus.Publish(new ClusterDeletedEvent
        {
            ClusterId = clusterId,
            ClusterName = "decommissioned-cluster",
            DeletedByEmail = "admin@example.com",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<ClusterDeletedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.ClusterDeleted);
        stored.Event.Should().Be("cluster.deleted");
        stored.Category.Should().Be("clusters");
        stored.Severity.Should().Be(Severity.Info);
        stored.User.Should().Be("admin@example.com");
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("decommissioned-cluster");
        stored.Message.Should().Contain("deleted");
    }

    private static ServiceProvider BuildProvider<TConsumer>(AuditDbContext db)
        where TConsumer : class, IConsumer
    {
        return new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IAuditEventRepository, MongoAuditEventRepository>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<TConsumer>())
            .BuildServiceProvider(true);
    }
}
