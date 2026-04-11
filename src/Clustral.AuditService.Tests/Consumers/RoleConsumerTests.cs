using Clustral.AuditService.Consumers;
using Clustral.AuditService.Domain;
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
public sealed class RoleConsumerTests(MongoFixture mongo, ITestOutputHelper output)
{
    [Fact]
    public async Task CreatedConsumer_PersistsAuditEvent_WithGroupList()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<RoleCreatedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var roleId = Guid.NewGuid();

        await harness.Bus.Publish(new RoleCreatedEvent
        {
            RoleId = roleId,
            Name = "developer",
            KubernetesGroups = ["dev-team", "viewers"],
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<RoleCreatedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event: {stored?.Message}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.RoleCreated);
        stored.Event.Should().Be("role.created");
        stored.Category.Should().Be("roles");
        stored.Severity.Should().Be(Severity.Info);
        stored.ResourceType.Should().Be("Role");
        stored.ResourceId.Should().Be(roleId);
        stored.ResourceName.Should().Be("developer");
        stored.Message.Should().Contain("developer").And.Contain("dev-team");
    }

    [Fact]
    public async Task UpdatedConsumer_PersistsAuditEvent_WithRoleName()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<RoleUpdatedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var roleId = Guid.NewGuid();

        await harness.Bus.Publish(new RoleUpdatedEvent
        {
            RoleId = roleId,
            Name = "senior-dev",
            Description = "Updated description",
            KubernetesGroups = ["dev-team", "admins"],
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<RoleUpdatedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event: {stored?.Message}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.RoleUpdated);
        stored.Event.Should().Be("role.updated");
        stored.Category.Should().Be("roles");
        stored.ResourceId.Should().Be(roleId);
        stored.ResourceName.Should().Be("senior-dev");
        stored.Message.Should().Contain("senior-dev");
    }

    [Fact]
    public async Task DeletedConsumer_PersistsAuditEvent()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<RoleDeletedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var roleId = Guid.NewGuid();

        await harness.Bus.Publish(new RoleDeletedEvent
        {
            RoleId = roleId,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<RoleDeletedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event: {stored?.Message}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.RoleDeleted);
        stored.Event.Should().Be("role.deleted");
        stored.Category.Should().Be("roles");
        stored.ResourceId.Should().Be(roleId);
        stored.Message.Should().Contain("deleted");
    }

    private static ServiceProvider BuildProvider<TConsumer>(AuditDbContext db)
        where TConsumer : class, IConsumer
    {
        return new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<TConsumer>())
            .BuildServiceProvider(true);
    }
}
