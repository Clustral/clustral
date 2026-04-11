using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Features.Clusters;
using Clustral.ControlPlane.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Clusters;

public sealed class ClusterAuditPublishTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RegisterCluster_PublishesClusterRegisteredEvent()
    {
        // Arrange
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new ClusterAuditHandler(NullLogger<ClusterAuditHandler>.Instance, publisher);

        var clusterId = Guid.NewGuid();
        var clusterName = "production-east";
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new ClusterRegistered(clusterId, clusterName)
        {
            OccurredAt = occurredAt,
        };

        // Act
        await handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        output.WriteLine($"Published {published.Count} message(s)");

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ClusterRegisteredEvent>().Subject;

        evt.ClusterId.Should().Be(clusterId);
        evt.Name.Should().Be(clusterName);
        evt.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public async Task DeleteCluster_PublishesClusterDeletedEvent()
    {
        // Arrange
        var published = new List<object>();
        var publisher = new FakePublishEndpoint(published);
        var handler = new ClusterAuditHandler(NullLogger<ClusterAuditHandler>.Instance, publisher);

        var clusterId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var domainEvent = new ClusterDeleted(clusterId)
        {
            OccurredAt = occurredAt,
        };

        // Act
        await handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        output.WriteLine($"Published {published.Count} message(s)");

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ClusterDeletedEvent>().Subject;

        evt.ClusterId.Should().Be(clusterId);
        evt.OccurredAt.Should().Be(occurredAt);
    }
}
