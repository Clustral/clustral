using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class CredentialRevokedConsumer(
    IAuditEventRepository repository,
    ILogger<CredentialRevokedConsumer> logger)
    : IConsumer<CredentialRevokedEvent>
{
    public async Task Consume(ConsumeContext<CredentialRevokedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "credential.revoked",
            code: EventCodes.CredentialRevoked,
            category: "credentials",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            user: evt.RevokedByEmail ?? evt.UserEmail,
            userId: evt.UserId != Guid.Empty ? evt.UserId : null,
            resourceType: "Credential",
            resourceId: evt.CredentialId,
            clusterId: evt.ClusterId != Guid.Empty ? evt.ClusterId : null,
            clusterName: evt.ClusterName,
            message: $"Credential {evt.CredentialId} revoked{(evt.Reason is not null ? $": {evt.Reason}" : "")}",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
