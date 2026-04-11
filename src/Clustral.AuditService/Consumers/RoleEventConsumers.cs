using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class RoleCreatedConsumer(
    IAuditEventRepository repository, ILogger<RoleCreatedConsumer> logger)
    : IConsumer<RoleCreatedEvent>
{
    public async Task Consume(ConsumeContext<RoleCreatedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "role.created",
            code: EventCodes.RoleCreated,
            category: "roles",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            resourceType: "Role",
            resourceId: evt.RoleId,
            resourceName: evt.Name,
            message: $"Role '{evt.Name}' created with groups [{string.Join(", ", evt.KubernetesGroups)}]",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class RoleUpdatedConsumer(
    IAuditEventRepository repository, ILogger<RoleUpdatedConsumer> logger)
    : IConsumer<RoleUpdatedEvent>
{
    public async Task Consume(ConsumeContext<RoleUpdatedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "role.updated",
            code: EventCodes.RoleUpdated,
            category: "roles",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            resourceType: "Role",
            resourceId: evt.RoleId,
            resourceName: evt.Name,
            message: $"Role {evt.RoleId} updated{(evt.Name is not null ? $" (name: {evt.Name})" : "")}",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class RoleDeletedConsumer(
    IAuditEventRepository repository, ILogger<RoleDeletedConsumer> logger)
    : IConsumer<RoleDeletedEvent>
{
    public async Task Consume(ConsumeContext<RoleDeletedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "role.deleted",
            code: EventCodes.RoleDeleted,
            category: "roles",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            resourceType: "Role",
            resourceId: evt.RoleId,
            message: $"Role {evt.RoleId} deleted",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
