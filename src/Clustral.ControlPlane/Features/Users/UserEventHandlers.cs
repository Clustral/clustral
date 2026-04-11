using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Infrastructure;
using MassTransit;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Users;

public sealed class UserAuditHandler(
    ILogger<UserAuditHandler> logger,
    IPublishEndpoint publisher,
    ClustralDb db)
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

        var user = await db.Users.Find(u => u.Id == e.UserId).FirstOrDefaultAsync(ct);
        var role = await db.Roles.Find(r => r.Id == e.RoleId).FirstOrDefaultAsync(ct);
        var cluster = await db.Clusters.Find(c => c.Id == e.ClusterId).FirstOrDefaultAsync(ct);

        await publisher.Publish(new RoleAssignedEvent
        {
            UserId = e.UserId,
            UserEmail = user?.Email,
            RoleId = e.RoleId,
            RoleName = role?.Name,
            ClusterId = e.ClusterId,
            ClusterName = cluster?.Name,
            AssignedBy = e.AssignedBy,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(RoleUnassigned e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role assignment {AssignmentId} removed", e.AssignmentId);

        // Domain event now carries IDs from the assignment (read before delete).
        var user = await db.Users.Find(u => u.Id == e.UserId).FirstOrDefaultAsync(ct);
        var role = await db.Roles.Find(r => r.Id == e.RoleId).FirstOrDefaultAsync(ct);
        var cluster = await db.Clusters.Find(c => c.Id == e.ClusterId).FirstOrDefaultAsync(ct);

        await publisher.Publish(new RoleUnassignedEvent
        {
            AssignmentId = e.AssignmentId,
            UserId = e.UserId,
            UserEmail = user?.Email,
            RoleId = e.RoleId,
            RoleName = role?.Name,
            ClusterId = e.ClusterId,
            ClusterName = cluster?.Name,
            RemovedByEmail = e.ActorEmail,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
