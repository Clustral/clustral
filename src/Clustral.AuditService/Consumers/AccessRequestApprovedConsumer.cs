using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestApprovedConsumer(
    AuditDbContext db,
    ILogger<AccessRequestApprovedConsumer> logger)
    : IConsumer<AccessRequestApprovedEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestApprovedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "access_request.approved",
            Code = EventCodes.AccessRequestApproved,
            Category = "access_requests",
            Severity = Severity.Info,
            Success = true,
            User = evt.ReviewerEmail,
            UserId = evt.ReviewerId,
            ResourceType = "AccessRequest",
            ResourceId = evt.RequestId,
            ClusterId = evt.ClusterId,
            ClusterName = evt.ClusterName,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Access request {evt.RequestId} approved by {evt.ReviewerEmail ?? evt.ReviewerId.ToString()}, grant expires {evt.GrantExpiresAt}",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
