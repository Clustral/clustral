using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Features.Auth;
using Clustral.ControlPlane.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Auth;

[Collection("Mongo")]
public sealed class CredentialAuditPublishTests(MongoFixture mongo, ITestOutputHelper output)
{
    [Fact]
    public async Task IssueCredential_PublishesCredentialIssuedEvent()
    {
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new CredentialAuditHandler(
            NullLogger<CredentialAuditHandler>.Instance, publisher, mongo.CreateDb());

        var credentialId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new CredentialIssued(credentialId, userId, clusterId, expiresAt)
        {
            OccurredAt = occurredAt,
        };

        await handler.Handle(domainEvent, CancellationToken.None);

        output.WriteLine($"Published {published.Count} message(s)");

        published.Should().ContainSingle()
            .Which.Should().BeOfType<CredentialIssuedEvent>()
            .Which.Should().BeEquivalentTo(new
            {
                CredentialId = credentialId,
                UserId = userId,
                ClusterId = clusterId,
                ExpiresAt = expiresAt,
                OccurredAt = occurredAt,
            });
    }

    [Fact]
    public async Task RevokeCredential_PublishesCredentialRevokedEvent()
    {
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new CredentialAuditHandler(
            NullLogger<CredentialAuditHandler>.Instance, publisher, mongo.CreateDb());

        var credentialId = Guid.NewGuid();
        var reason = "User offboarded";
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new CredentialRevoked(credentialId, reason)
        {
            OccurredAt = occurredAt,
        };

        await handler.Handle(domainEvent, CancellationToken.None);

        output.WriteLine($"Published {published.Count} message(s)");

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<CredentialRevokedEvent>().Subject;

        evt.CredentialId.Should().Be(credentialId);
        evt.Reason.Should().Be(reason);
        evt.OccurredAt.Should().Be(occurredAt);
    }
}
