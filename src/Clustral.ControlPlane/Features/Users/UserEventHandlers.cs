using Clustral.ControlPlane.Domain.Events;
using MediatR;

namespace Clustral.ControlPlane.Features.Users;

public sealed class UserAuditHandler(ILogger<UserAuditHandler> logger)
    : INotificationHandler<UserSynced>,
      INotificationHandler<RoleAssigned>,
      INotificationHandler<RoleUnassigned>
{
    public Task Handle(UserSynced e, CancellationToken ct)
    {
        if (e.IsNew)
            logger.LogInformation("[Audit] User {UserId} created from OIDC subject {Subject}", e.UserId, e.Subject);
        else
            logger.LogInformation("[Audit] User {UserId} synced from OIDC subject {Subject}", e.UserId, e.Subject);
        return Task.CompletedTask;
    }

    public Task Handle(RoleAssigned e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} assigned to user {UserId} on cluster {ClusterId} by {AssignedBy}",
            e.RoleId, e.UserId, e.ClusterId, e.AssignedBy);
        return Task.CompletedTask;
    }

    public Task Handle(RoleUnassigned e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role assignment {AssignmentId} removed", e.AssignmentId);
        return Task.CompletedTask;
    }
}
