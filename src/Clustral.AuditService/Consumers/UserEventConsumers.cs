using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class UserSyncedConsumer(
    IAuditEventRepository repository, ILogger<UserSyncedConsumer> logger)
    : IConsumer<UserSyncedEvent>
{
    public async Task Consume(ConsumeContext<UserSyncedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "user.synced",
            code: EventCodes.UserSynced,
            category: "auth",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            user: evt.Email ?? evt.Subject,
            userId: evt.UserId,
            resourceType: "User",
            resourceId: evt.UserId,
            message: evt.IsNew
                ? $"New user {evt.Email ?? evt.Subject} created from OIDC"
                : $"User {evt.Email ?? evt.Subject} synced",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class RoleAssignedConsumer(
    IAuditEventRepository repository, ILogger<RoleAssignedConsumer> logger)
    : IConsumer<RoleAssignedEvent>
{
    public async Task Consume(ConsumeContext<RoleAssignedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "user.role_assigned",
            code: EventCodes.RoleAssigned,
            category: "auth",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            user: evt.AssignedBy,
            userId: evt.UserId,
            resourceType: "User",
            resourceId: evt.UserId,
            clusterId: evt.ClusterId,
            clusterName: evt.ClusterName,
            message: $"Role {evt.RoleName ?? evt.RoleId.ToString()} assigned to user {evt.UserEmail ?? evt.UserId.ToString()} on cluster {evt.ClusterName ?? evt.ClusterId.ToString()} by {evt.AssignedBy}",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class RoleUnassignedConsumer(
    IAuditEventRepository repository, ILogger<RoleUnassignedConsumer> logger)
    : IConsumer<RoleUnassignedEvent>
{
    public async Task Consume(ConsumeContext<RoleUnassignedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "user.role_unassigned",
            code: EventCodes.RoleUnassigned,
            category: "auth",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            user: evt.RemovedByEmail ?? evt.UserEmail,
            userId: evt.UserId != Guid.Empty ? evt.UserId : null,
            resourceType: "RoleAssignment",
            resourceId: evt.AssignmentId,
            clusterId: evt.ClusterId != Guid.Empty ? evt.ClusterId : null,
            clusterName: evt.ClusterName,
            message: $"Role {evt.RoleName ?? evt.RoleId.ToString()} unassigned from user {evt.UserEmail ?? evt.UserId.ToString()} on cluster {evt.ClusterName ?? evt.ClusterId.ToString()}",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
