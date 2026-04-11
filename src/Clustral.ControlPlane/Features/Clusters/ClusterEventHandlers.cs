using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Infrastructure;
using MassTransit;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Clusters;

public sealed class ClusterAuditHandler(
    ILogger<ClusterAuditHandler> logger,
    IPublishEndpoint publisher,
    ClustralDb db)
    : INotificationHandler<ClusterRegistered>,
      INotificationHandler<ClusterConnected>,
      INotificationHandler<ClusterDisconnected>,
      INotificationHandler<ClusterDeleted>
{
    public async Task Handle(ClusterRegistered e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Cluster {ClusterId} registered: {Name}", e.ClusterId, e.Name);

        await publisher.Publish(new ClusterRegisteredEvent
        {
            ClusterId = e.ClusterId,
            Name = e.Name,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(ClusterConnected e, CancellationToken ct)
    {
        var cluster = await db.Clusters.Find(c => c.Id == e.ClusterId).FirstOrDefaultAsync(ct);

        logger.LogInformation("[Audit] Cluster {ClusterId} ({Name}) connected (k8s: {Version})",
            e.ClusterId, cluster?.Name ?? "unknown", e.KubernetesVersion ?? "unknown");

        await publisher.Publish(new ClusterConnectedEvent
        {
            ClusterId = e.ClusterId,
            ClusterName = cluster?.Name,
            KubernetesVersion = e.KubernetesVersion,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(ClusterDisconnected e, CancellationToken ct)
    {
        var cluster = await db.Clusters.Find(c => c.Id == e.ClusterId).FirstOrDefaultAsync(ct);

        logger.LogInformation("[Audit] Cluster {ClusterId} ({Name}) disconnected",
            e.ClusterId, cluster?.Name ?? "unknown");

        await publisher.Publish(new ClusterDisconnectedEvent
        {
            ClusterId = e.ClusterId,
            ClusterName = cluster?.Name,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(ClusterDeleted e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Cluster {ClusterId} ({Name}) deleted",
            e.ClusterId, e.ClusterName ?? "unknown");

        await publisher.Publish(new ClusterDeletedEvent
        {
            ClusterId = e.ClusterId,
            ClusterName = e.ClusterName,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
