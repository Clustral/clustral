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
public sealed class UserConsumerTests(MongoFixture mongo, ITestOutputHelper output)
{
    [Fact]
    public async Task SyncedConsumer_NewUser_PersistsAuditEvent_WithCreatedMessage()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<UserSyncedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var userId = Guid.NewGuid();

        await harness.Bus.Publish(new UserSyncedEvent
        {
            UserId = userId,
            Subject = "auth0|12345",
            Email = "newuser@example.com",
            IsNew = true,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<UserSyncedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event: {stored?.Message}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.UserSynced);
        stored.Event.Should().Be("user.synced");
        stored.Category.Should().Be("auth");
        stored.Severity.Should().Be(Severity.Info);
        stored.User.Should().Be("newuser@example.com");
        stored.UserId.Should().Be(userId);
        stored.ResourceType.Should().Be("User");
        stored.ResourceId.Should().Be(userId);
        stored.Message.Should().Contain("New user").And.Contain("newuser@example.com");
    }

    [Fact]
    public async Task SyncedConsumer_ExistingUser_PersistsAuditEvent_WithSyncedMessage()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<UserSyncedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var userId = Guid.NewGuid();

        await harness.Bus.Publish(new UserSyncedEvent
        {
            UserId = userId,
            Subject = "auth0|12345",
            Email = "existing@example.com",
            IsNew = false,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<UserSyncedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event: {stored?.Message}");

        stored.Should().NotBeNull();
        stored!.Message.Should().NotContain("New user");
        stored.Message.Should().Contain("synced");
    }

    [Fact]
    public async Task SyncedConsumer_NoEmail_FallsBackToSubject()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<UserSyncedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var userId = Guid.NewGuid();

        await harness.Bus.Publish(new UserSyncedEvent
        {
            UserId = userId,
            Subject = "auth0|99999",
            Email = null,
            IsNew = true,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<UserSyncedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        stored.Should().NotBeNull();
        stored!.User.Should().Be("auth0|99999");
        stored.Message.Should().Contain("auth0|99999");
    }

    [Fact]
    public async Task RoleAssignedConsumer_PersistsAuditEvent_WithClusterAndRole()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<RoleAssignedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();

        await harness.Bus.Publish(new RoleAssignedEvent
        {
            UserId = userId,
            UserEmail = "alice@example.com",
            RoleId = roleId,
            RoleName = "admin",
            ClusterId = clusterId,
            ClusterName = "prod",
            AssignedBy = "bob@example.com",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<RoleAssignedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event: {stored?.Message}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.RoleAssigned);
        stored.Event.Should().Be("user.role_assigned");
        stored.Category.Should().Be("auth");
        stored.User.Should().Be("bob@example.com");
        stored.UserId.Should().Be(userId);
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("prod");
        stored.Message.Should().Contain("admin").And.Contain("alice@example.com").And.Contain("prod");
    }

    [Fact]
    public async Task RoleUnassignedConsumer_PersistsAuditEvent_WithUserAndCluster()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<RoleUnassignedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var assignmentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();

        await harness.Bus.Publish(new RoleUnassignedEvent
        {
            AssignmentId = assignmentId,
            UserId = userId,
            UserEmail = "alice@example.com",
            RoleId = roleId,
            RoleName = "developer",
            ClusterId = clusterId,
            ClusterName = "prod",
            RemovedByEmail = "admin@example.com",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<RoleUnassignedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event: {stored?.Message}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.RoleUnassigned);
        stored.Event.Should().Be("user.role_unassigned");
        stored.Category.Should().Be("auth");
        stored.User.Should().Be("admin@example.com");
        stored.UserId.Should().Be(userId);
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("prod");
        stored.ResourceType.Should().Be("RoleAssignment");
        stored.ResourceId.Should().Be(assignmentId);
        stored.Message.Should().Contain("developer").And.Contain("alice@example.com");
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
