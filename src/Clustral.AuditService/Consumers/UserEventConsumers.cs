using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class UserSyncedConsumer(
    AuditDbContext db, ILogger<UserSyncedConsumer> logger)
    : IConsumer<UserSyncedEvent>
{
    public async Task Consume(ConsumeContext<UserSyncedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "user.synced",
            Code = EventCodes.UserSynced,
            Category = "auth",
            Severity = Severity.Info,
            Success = true,
            User = evt.Email ?? evt.Subject,
            UserId = evt.UserId,
            ResourceType = "User",
            ResourceId = evt.UserId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = evt.IsNew
                ? $"New user {evt.Email ?? evt.Subject} created from OIDC"
                : $"User {evt.Email ?? evt.Subject} synced",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class RoleAssignedConsumer(
    AuditDbContext db, ILogger<RoleAssignedConsumer> logger)
    : IConsumer<RoleAssignedEvent>
{
    public async Task Consume(ConsumeContext<RoleAssignedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "user.role_assigned",
            Code = EventCodes.RoleAssigned,
            Category = "auth",
            Severity = Severity.Info,
            Success = true,
            User = evt.AssignedBy,
            UserId = evt.UserId,
            ResourceType = "User",
            ResourceId = evt.UserId,
            ClusterId = evt.ClusterId,
            ClusterName = evt.ClusterName,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Role {evt.RoleName ?? evt.RoleId.ToString()} assigned to user {evt.UserEmail ?? evt.UserId.ToString()} on cluster {evt.ClusterName ?? evt.ClusterId.ToString()} by {evt.AssignedBy}",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class RoleUnassignedConsumer(
    AuditDbContext db, ILogger<RoleUnassignedConsumer> logger)
    : IConsumer<RoleUnassignedEvent>
{
    public async Task Consume(ConsumeContext<RoleUnassignedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "user.role_unassigned",
            Code = EventCodes.RoleUnassigned,
            Category = "auth",
            Severity = Severity.Info,
            Success = true,
            ResourceType = "RoleAssignment",
            ResourceId = evt.AssignmentId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Role assignment {evt.AssignmentId} removed",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
