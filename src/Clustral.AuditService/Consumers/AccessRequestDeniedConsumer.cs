using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestDeniedConsumer(
    IAuditEventRepository repository,
    ILogger<AccessRequestDeniedConsumer> logger)
    : IConsumer<AccessRequestDeniedEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestDeniedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "access_request.denied",
            code: EventCodes.AccessRequestDenied,
            category: "access_requests",
            severity: Severity.Warning,
            success: false,
            time: evt.OccurredAt,
            user: evt.ReviewerEmail,
            userId: evt.ReviewerId,
            resourceType: "AccessRequest",
            resourceId: evt.RequestId,
            message: $"Access request {evt.RequestId} denied: {evt.Reason}",
            error: evt.Reason,
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
