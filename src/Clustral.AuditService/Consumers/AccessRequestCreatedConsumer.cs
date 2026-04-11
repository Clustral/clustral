using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestCreatedConsumer(
    AuditDbContext db,
    ILogger<AccessRequestCreatedConsumer> logger)
    : IConsumer<AccessRequestCreatedEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestCreatedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "access_request.created",
            Code = EventCodes.AccessRequestCreated,
            Category = "access_requests",
            Severity = Severity.Info,
            Success = true,
            User = evt.RequesterEmail,
            UserId = evt.RequesterId,
            ResourceType = "AccessRequest",
            ResourceId = evt.RequestId,
            ClusterId = evt.ClusterId,
            ClusterName = evt.ClusterName,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Access request {evt.RequestId} created for role {evt.RoleName ?? evt.RoleId.ToString()} on cluster {evt.ClusterName ?? evt.ClusterId.ToString()}",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
