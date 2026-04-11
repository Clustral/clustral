using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestRevokedConsumer(
    AuditDbContext db,
    ILogger<AccessRequestRevokedConsumer> logger)
    : IConsumer<AccessRequestRevokedEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestRevokedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "access_request.revoked",
            Code = EventCodes.AccessRequestRevoked,
            Category = "access_requests",
            Severity = Severity.Info,
            Success = true,
            User = evt.RevokedByEmail,
            UserId = evt.RevokedById,
            ResourceType = "AccessRequest",
            ResourceId = evt.RequestId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Access request {evt.RequestId} revoked{(evt.Reason is not null ? $": {evt.Reason}" : "")}",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
