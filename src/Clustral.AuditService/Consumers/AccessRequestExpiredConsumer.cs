using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestExpiredConsumer(
    IAuditEventRepository repository,
    ILogger<AccessRequestExpiredConsumer> logger)
    : IConsumer<AccessRequestExpiredEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestExpiredEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "access_request.expired",
            code: EventCodes.AccessRequestExpired,
            category: "access_requests",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            resourceType: "AccessRequest",
            resourceId: evt.RequestId,
            message: $"Access request {evt.RequestId} expired",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
