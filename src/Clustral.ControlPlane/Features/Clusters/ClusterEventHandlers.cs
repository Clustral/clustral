using Clustral.ControlPlane.Domain.Events;
using MediatR;

namespace Clustral.ControlPlane.Features.Clusters;

public sealed class ClusterAuditHandler(ILogger<ClusterAuditHandler> logger)
    : INotificationHandler<ClusterRegistered>,
      INotificationHandler<ClusterConnected>,
      INotificationHandler<ClusterDisconnected>,
      INotificationHandler<ClusterDeleted>
{
    public Task Handle(ClusterRegistered e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Cluster {ClusterId} registered: {Name}", e.ClusterId, e.Name);
        return Task.CompletedTask;
    }

    public Task Handle(ClusterConnected e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Cluster {ClusterId} connected (k8s: {Version})",
            e.ClusterId, e.KubernetesVersion ?? "unknown");
        return Task.CompletedTask;
    }

    public Task Handle(ClusterDisconnected e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Cluster {ClusterId} disconnected", e.ClusterId);
        return Task.CompletedTask;
    }

    public Task Handle(ClusterDeleted e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Cluster {ClusterId} deleted", e.ClusterId);
        return Task.CompletedTask;
    }
}
