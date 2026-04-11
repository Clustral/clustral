using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Features.Roles;
using Clustral.ControlPlane.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Roles;

public sealed class RoleAuditPublishTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CreateRole_PublishesRoleCreatedEvent()
    {
        // Arrange
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new RoleAuditHandler(NullLogger<RoleAuditHandler>.Instance, publisher);

        var roleId = Guid.NewGuid();
        var roleName = "admin";
        var groups = new List<string> { "system:masters", "cluster-admins" };
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new RoleCreated(roleId, roleName, groups)
        {
            OccurredAt = occurredAt,
        };

        // Act
        await handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        output.WriteLine($"Published {published.Count} message(s)");

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<RoleCreatedEvent>().Subject;

        evt.RoleId.Should().Be(roleId);
        evt.Name.Should().Be(roleName);
        evt.KubernetesGroups.Should().BeEquivalentTo(groups);
        evt.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public async Task DeleteRole_PublishesRoleDeletedEvent()
    {
        // Arrange
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new RoleAuditHandler(NullLogger<RoleAuditHandler>.Instance, publisher);

        var roleId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new RoleDeleted(roleId)
        {
            OccurredAt = occurredAt,
        };

        // Act
        await handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        output.WriteLine($"Published {published.Count} message(s)");

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<RoleDeletedEvent>().Subject;

        evt.RoleId.Should().Be(roleId);
        evt.OccurredAt.Should().Be(occurredAt);
    }
}
