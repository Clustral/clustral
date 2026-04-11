using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class CredentialRevokedConsumer(
    AuditDbContext db,
    ILogger<CredentialRevokedConsumer> logger)
    : IConsumer<CredentialRevokedEvent>
{
    public async Task Consume(ConsumeContext<CredentialRevokedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "credential.revoked",
            Code = EventCodes.CredentialRevoked,
            Category = "credentials",
            Severity = Severity.Info,
            Success = true,
            ResourceType = "Credential",
            ResourceId = evt.CredentialId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Credential {evt.CredentialId} revoked{(evt.Reason is not null ? $": {evt.Reason}" : "")}",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
