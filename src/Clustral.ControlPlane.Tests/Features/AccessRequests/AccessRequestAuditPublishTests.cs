using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Features.AccessRequests;
using Clustral.ControlPlane.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.AccessRequests;

[Collection("Mongo")]
public sealed class AccessRequestAuditPublishTests(MongoFixture mongo, ITestOutputHelper output)
{
    [Fact]
    public async Task CreateAccessRequest_PublishesAccessRequestCreatedEvent()
    {
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new AccessRequestAuditHandler(
            NullLogger<AccessRequestAuditHandler>.Instance, publisher, mongo.CreateDb());

        var requestId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();
        var reason = "Need access for deployment";
        var duration = TimeSpan.FromHours(4);
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new AccessRequestCreated(requestId, requesterId, roleId, clusterId, reason, duration)
        {
            OccurredAt = occurredAt,
        };

        await handler.Handle(domainEvent, CancellationToken.None);

        output.WriteLine($"Published {published.Count} message(s)");

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<AccessRequestCreatedEvent>().Subject;

        evt.RequestId.Should().Be(requestId);
        evt.RequesterId.Should().Be(requesterId);
        evt.RoleId.Should().Be(roleId);
        evt.ClusterId.Should().Be(clusterId);
        evt.Reason.Should().Be(reason);
        evt.RequestedDuration.Should().Be(duration);
        evt.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public async Task ApproveAccessRequest_PublishesAccessRequestApprovedEvent()
    {
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new AccessRequestAuditHandler(
            NullLogger<AccessRequestAuditHandler>.Instance, publisher, mongo.CreateDb());

        var requestId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var grantDuration = TimeSpan.FromHours(2);
        var grantExpiresAt = DateTimeOffset.UtcNow.AddHours(2);
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new AccessRequestApproved(requestId, reviewerId, grantDuration, grantExpiresAt)
        {
            OccurredAt = occurredAt,
        };

        await handler.Handle(domainEvent, CancellationToken.None);

        output.WriteLine($"Published {published.Count} message(s)");

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<AccessRequestApprovedEvent>().Subject;

        evt.RequestId.Should().Be(requestId);
        evt.ReviewerId.Should().Be(reviewerId);
        evt.GrantDuration.Should().Be(grantDuration);
        evt.GrantExpiresAt.Should().Be(grantExpiresAt);
        evt.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public async Task DenyAccessRequest_PublishesAccessRequestDeniedEvent()
    {
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new AccessRequestAuditHandler(
            NullLogger<AccessRequestAuditHandler>.Instance, publisher, mongo.CreateDb());

        var requestId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var reason = "Insufficient justification";
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new AccessRequestDenied(requestId, reviewerId, reason)
        {
            OccurredAt = occurredAt,
        };

        await handler.Handle(domainEvent, CancellationToken.None);

        output.WriteLine($"Published {published.Count} message(s)");

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<AccessRequestDeniedEvent>().Subject;

        evt.RequestId.Should().Be(requestId);
        evt.ReviewerId.Should().Be(reviewerId);
        evt.Reason.Should().Be(reason);
        evt.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public async Task RevokeAccessRequest_PublishesAccessRequestRevokedEvent()
    {
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new AccessRequestAuditHandler(
            NullLogger<AccessRequestAuditHandler>.Instance, publisher, mongo.CreateDb());

        var requestId = Guid.NewGuid();
        var revokedById = Guid.NewGuid();
        var reason = "Security incident";
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new AccessRequestRevoked(requestId, revokedById, reason)
        {
            OccurredAt = occurredAt,
        };

        await handler.Handle(domainEvent, CancellationToken.None);

        output.WriteLine($"Published {published.Count} message(s)");

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<AccessRequestRevokedEvent>().Subject;

        evt.RequestId.Should().Be(requestId);
        evt.RevokedById.Should().Be(revokedById);
        evt.Reason.Should().Be(reason);
        evt.OccurredAt.Should().Be(occurredAt);
    }
}
