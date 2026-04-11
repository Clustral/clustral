using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class CredentialIssuedConsumer(
    IAuditEventRepository repository,
    ILogger<CredentialIssuedConsumer> logger)
    : IConsumer<CredentialIssuedEvent>
{
    public async Task Consume(ConsumeContext<CredentialIssuedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "credential.issued",
            code: EventCodes.CredentialIssued,
            category: "credentials",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            user: evt.UserEmail,
            userId: evt.UserId,
            resourceType: "Credential",
            resourceId: evt.CredentialId,
            clusterId: evt.ClusterId,
            clusterName: evt.ClusterName,
            message: $"Credential {evt.CredentialId} issued for user {evt.UserEmail ?? evt.UserId.ToString()}, expires {evt.ExpiresAt}",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
