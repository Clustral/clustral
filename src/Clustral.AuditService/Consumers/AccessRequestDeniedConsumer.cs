using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestDeniedConsumer(
    AuditDbContext db,
    ILogger<AccessRequestDeniedConsumer> logger)
    : IConsumer<AccessRequestDeniedEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestDeniedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "access_request.denied",
            Code = EventCodes.AccessRequestDenied,
            Category = "access_requests",
            Severity = Severity.Warning,
            Success = false,
            User = evt.ReviewerEmail,
            UserId = evt.ReviewerId,
            ResourceType = "AccessRequest",
            ResourceId = evt.RequestId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Access request {evt.RequestId} denied: {evt.Reason}",
            Error = evt.Reason,
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
