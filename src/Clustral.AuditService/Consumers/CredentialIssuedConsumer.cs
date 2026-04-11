using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class CredentialIssuedConsumer(
    AuditDbContext db,
    ILogger<CredentialIssuedConsumer> logger)
    : IConsumer<CredentialIssuedEvent>
{
    public async Task Consume(ConsumeContext<CredentialIssuedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "credential.issued",
            Code = EventCodes.CredentialIssued,
            Category = "credentials",
            Severity = Severity.Info,
            Success = true,
            User = evt.UserEmail,
            UserId = evt.UserId,
            ResourceType = "Credential",
            ResourceId = evt.CredentialId,
            ClusterId = evt.ClusterId,
            ClusterName = evt.ClusterName,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Credential {evt.CredentialId} issued for user {evt.UserEmail ?? evt.UserId.ToString()}, expires {evt.ExpiresAt}",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
