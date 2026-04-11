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
      INotificationHandler<ClusterDeleted>,
      INotificationHandler<AgentAuthFailed>
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
        logger.LogInformation("[Audit] Cluster {ClusterId} ({Name}) deleted by {Actor}",
            e.ClusterId, e.ClusterName ?? "unknown", e.ActorEmail ?? "unknown");

        await publisher.Publish(new ClusterDeletedEvent
        {
            ClusterId = e.ClusterId,
            ClusterName = e.ClusterName,
            DeletedByEmail = e.ActorEmail,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(AgentAuthFailed e, CancellationToken ct)
    {
        var cluster = e.ClusterId.HasValue
            ? await db.Clusters.Find(c => c.Id == e.ClusterId).FirstOrDefaultAsync(ct)
            : null;

        logger.LogWarning("[Audit] Agent auth failed for cluster {ClusterId}: {Reason}",
            e.ClusterId?.ToString() ?? "unknown", e.Reason);

        await publisher.Publish(new AgentAuthFailedEvent
        {
            ClusterId = e.ClusterId,
            ClusterName = cluster?.Name,
            Reason = e.Reason,
            CertCN = e.CertCN,
            RemoteIp = e.RemoteIp,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
