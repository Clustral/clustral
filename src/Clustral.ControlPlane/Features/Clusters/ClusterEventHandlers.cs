using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using MassTransit;
using MediatR;

namespace Clustral.ControlPlane.Features.Clusters;

public sealed class ClusterAuditHandler(ILogger<ClusterAuditHandler> logger, IPublishEndpoint publisher)
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
        logger.LogInformation("[Audit] Cluster {ClusterId} connected (k8s: {Version})",
            e.ClusterId, e.KubernetesVersion ?? "unknown");

        await publisher.Publish(new ClusterConnectedEvent
        {
            ClusterId = e.ClusterId,
            KubernetesVersion = e.KubernetesVersion,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(ClusterDisconnected e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Cluster {ClusterId} disconnected", e.ClusterId);

        await publisher.Publish(new ClusterDisconnectedEvent
        {
            ClusterId = e.ClusterId,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(ClusterDeleted e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Cluster {ClusterId} deleted", e.ClusterId);

        await publisher.Publish(new ClusterDeletedEvent
        {
            ClusterId = e.ClusterId,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
