using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class CredentialRevokeDeniedConsumer(
    IAuditEventRepository repository,
    ILogger<CredentialRevokeDeniedConsumer> logger)
    : IConsumer<CredentialRevokeDeniedEvent>
{
    public async Task Consume(ConsumeContext<CredentialRevokeDeniedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "credential.revoke_denied",
            code: EventCodes.CredentialRevokeDenied,
            category: "credentials",
            severity: Severity.Warning,
            success: false,
            time: evt.OccurredAt,
            user: evt.ActorEmail,
            resourceType: "Credential",
            resourceId: evt.CredentialId,
            message: $"Credential revocation denied: {evt.Reason}",
            error: evt.Reason,
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
