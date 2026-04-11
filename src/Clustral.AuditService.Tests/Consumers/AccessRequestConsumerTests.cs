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
public sealed class AccessRequestConsumerTests(MongoFixture mongo, ITestOutputHelper output)
{
    [Fact]
    public async Task CreatedConsumer_PersistsAuditEvent_WithCorrectFields()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<AccessRequestCreatedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var requestId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await harness.Bus.Publish(new AccessRequestCreatedEvent
        {
            RequestId = requestId,
            RequesterId = requesterId,
            RequesterEmail = "alice@example.com",
            RoleId = roleId,
            RoleName = "admin",
            ClusterId = clusterId,
            ClusterName = "prod",
            Reason = "deploy fix",
            RequestedDuration = TimeSpan.FromHours(4),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<AccessRequestCreatedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.AccessRequestCreated);
        stored.Event.Should().Be("access_request.created");
        stored.Category.Should().Be("access_requests");
        stored.Severity.Should().Be(Severity.Info);
        stored.Success.Should().BeTrue();
        stored.User.Should().Be("alice@example.com");
        stored.UserId.Should().Be(requesterId);
        stored.ResourceType.Should().Be("AccessRequest");
        stored.ResourceId.Should().Be(requestId);
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("prod");
        stored.Message.Should().Contain("admin");
    }

    [Fact]
    public async Task ApprovedConsumer_PersistsAuditEvent_WithCorrectFields()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<AccessRequestApprovedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var requestId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);

        await harness.Bus.Publish(new AccessRequestApprovedEvent
        {
            RequestId = requestId,
            ReviewerId = reviewerId,
            ReviewerEmail = "bob@example.com",
            ClusterId = clusterId,
            ClusterName = "staging",
            GrantDuration = TimeSpan.FromHours(8),
            GrantExpiresAt = expiresAt,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<AccessRequestApprovedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.AccessRequestApproved);
        stored.Event.Should().Be("access_request.approved");
        stored.Category.Should().Be("access_requests");
        stored.Severity.Should().Be(Severity.Info);
        stored.User.Should().Be("bob@example.com");
        stored.UserId.Should().Be(reviewerId);
        stored.ResourceId.Should().Be(requestId);
        stored.ClusterId.Should().Be(clusterId);
        stored.Message.Should().Contain("approved");
    }

    [Fact]
    public async Task DeniedConsumer_PersistsAuditEvent_WithWarningSeverity()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<AccessRequestDeniedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var requestId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();

        var clusterId = Guid.NewGuid();

        await harness.Bus.Publish(new AccessRequestDeniedEvent
        {
            RequestId = requestId,
            ReviewerId = reviewerId,
            ReviewerEmail = "bob@example.com",
            ClusterId = clusterId,
            ClusterName = "prod",
            Reason = "Insufficient justification",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<AccessRequestDeniedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.AccessRequestDenied);
        stored.Event.Should().Be("access_request.denied");
        stored.Category.Should().Be("access_requests");
        stored.Severity.Should().Be(Severity.Warning);
        stored.Success.Should().BeFalse();
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("prod");
        stored.Error.Should().Be("Insufficient justification");
        stored.Message.Should().Contain("denied");
    }

    [Fact]
    public async Task RevokedConsumer_PersistsAuditEvent_WithReason()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<AccessRequestRevokedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var requestId = Guid.NewGuid();
        var revokedById = Guid.NewGuid();

        var clusterId = Guid.NewGuid();

        await harness.Bus.Publish(new AccessRequestRevokedEvent
        {
            RequestId = requestId,
            RevokedById = revokedById,
            RevokedByEmail = "admin@example.com",
            ClusterId = clusterId,
            ClusterName = "staging",
            Reason = "Policy violation",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<AccessRequestRevokedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.AccessRequestRevoked);
        stored.Event.Should().Be("access_request.revoked");
        stored.Category.Should().Be("access_requests");
        stored.Severity.Should().Be(Severity.Info);
        stored.User.Should().Be("admin@example.com");
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("staging");
        stored.Message.Should().Contain("Policy violation");
    }

    [Fact]
    public async Task ExpiredConsumer_PersistsAuditEvent_WithUserAndCluster()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<AccessRequestExpiredConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var requestId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();

        await harness.Bus.Publish(new AccessRequestExpiredEvent
        {
            RequestId = requestId,
            RequesterId = requesterId,
            RequesterEmail = "alice@example.com",
            ClusterId = clusterId,
            ClusterName = "prod",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<AccessRequestExpiredEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.AccessRequestExpired);
        stored.Event.Should().Be("access_request.expired");
        stored.Category.Should().Be("access_requests");
        stored.Severity.Should().Be(Severity.Info);
        stored.User.Should().Be("alice@example.com");
        stored.UserId.Should().Be(requesterId);
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("prod");
        stored.ResourceId.Should().Be(requestId);
        stored.Message.Should().Contain("expired");
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
