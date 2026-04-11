using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class RoleCreatedConsumer(
    AuditDbContext db, ILogger<RoleCreatedConsumer> logger)
    : IConsumer<RoleCreatedEvent>
{
    public async Task Consume(ConsumeContext<RoleCreatedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "role.created",
            Code = EventCodes.RoleCreated,
            Category = "roles",
            Severity = Severity.Info,
            Success = true,
            ResourceType = "Role",
            ResourceId = evt.RoleId,
            ResourceName = evt.Name,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Role '{evt.Name}' created with groups [{string.Join(", ", evt.KubernetesGroups)}]",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class RoleUpdatedConsumer(
    AuditDbContext db, ILogger<RoleUpdatedConsumer> logger)
    : IConsumer<RoleUpdatedEvent>
{
    public async Task Consume(ConsumeContext<RoleUpdatedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "role.updated",
            Code = EventCodes.RoleUpdated,
            Category = "roles",
            Severity = Severity.Info,
            Success = true,
            ResourceType = "Role",
            ResourceId = evt.RoleId,
            ResourceName = evt.Name,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Role {evt.RoleId} updated{(evt.Name is not null ? $" (name: {evt.Name})" : "")}",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class RoleDeletedConsumer(
    AuditDbContext db, ILogger<RoleDeletedConsumer> logger)
    : IConsumer<RoleDeletedEvent>
{
    public async Task Consume(ConsumeContext<RoleDeletedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "role.deleted",
            Code = EventCodes.RoleDeleted,
            Category = "roles",
            Severity = Severity.Info,
            Success = true,
            ResourceType = "Role",
            ResourceId = evt.RoleId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Role {evt.RoleId} deleted",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
