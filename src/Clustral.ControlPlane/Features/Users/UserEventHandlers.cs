using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using MassTransit;
using MediatR;

namespace Clustral.ControlPlane.Features.Users;

public sealed class UserAuditHandler(ILogger<UserAuditHandler> logger, IPublishEndpoint publisher)
    : INotificationHandler<UserSynced>,
      INotificationHandler<RoleAssigned>,
      INotificationHandler<RoleUnassigned>
{
    public async Task Handle(UserSynced e, CancellationToken ct)
    {
        if (e.IsNew)
            logger.LogInformation("[Audit] User {UserId} created from OIDC subject {Subject}", e.UserId, e.Subject);
        else
            logger.LogInformation("[Audit] User {UserId} synced from OIDC subject {Subject}", e.UserId, e.Subject);

        await publisher.Publish(new UserSyncedEvent
        {
            UserId = e.UserId,
            Subject = e.Subject,
            Email = e.Email,
            IsNew = e.IsNew,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(RoleAssigned e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} assigned to user {UserId} on cluster {ClusterId} by {AssignedBy}",
            e.RoleId, e.UserId, e.ClusterId, e.AssignedBy);

        await publisher.Publish(new RoleAssignedEvent
        {
            UserId = e.UserId,
            UserEmail = null,
            RoleId = e.RoleId,
            RoleName = null,
            ClusterId = e.ClusterId,
            ClusterName = null,
            AssignedBy = e.AssignedBy,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(RoleUnassigned e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role assignment {AssignmentId} removed", e.AssignmentId);

        await publisher.Publish(new RoleUnassignedEvent
        {
            AssignmentId = e.AssignmentId,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
