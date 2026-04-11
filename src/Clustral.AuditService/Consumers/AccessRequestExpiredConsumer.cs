using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestExpiredConsumer(
    AuditDbContext db,
    ILogger<AccessRequestExpiredConsumer> logger)
    : IConsumer<AccessRequestExpiredEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestExpiredEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "access_request.expired",
            Code = EventCodes.AccessRequestExpired,
            Category = "access_requests",
            Severity = Severity.Info,
            Success = true,
            ResourceType = "AccessRequest",
            ResourceId = evt.RequestId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Access request {evt.RequestId} expired",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
