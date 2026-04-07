using Clustral.ControlPlane.Domain.Events;
using MediatR;

namespace Clustral.ControlPlane.Features.Roles;

public sealed class RoleAuditHandler(ILogger<RoleAuditHandler> logger)
    : INotificationHandler<RoleCreated>,
      INotificationHandler<RoleUpdated>,
      INotificationHandler<RoleDeleted>
{
    public Task Handle(RoleCreated e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} created: {Name}, groups: [{Groups}]",
            e.RoleId, e.Name, string.Join(", ", e.KubernetesGroups));
        return Task.CompletedTask;
    }

    public Task Handle(RoleUpdated e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} updated", e.RoleId);
        return Task.CompletedTask;
    }

    public Task Handle(RoleDeleted e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} deleted", e.RoleId);
        return Task.CompletedTask;
    }
}
