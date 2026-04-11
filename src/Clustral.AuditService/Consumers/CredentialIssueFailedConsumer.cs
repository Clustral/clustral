using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class CredentialIssueFailedConsumer(
    IAuditEventRepository repository,
    ILogger<CredentialIssueFailedConsumer> logger)
    : IConsumer<CredentialIssueFailedEvent>
{
    public async Task Consume(ConsumeContext<CredentialIssueFailedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "credential.issue_failed",
            code: EventCodes.CredentialIssueFailed,
            category: "credentials",
            severity: Severity.Warning,
            success: false,
            time: evt.OccurredAt,
            user: evt.ActorEmail,
            clusterId: evt.ClusterId,
            clusterName: evt.ClusterName,
            message: $"Credential issuance failed: {evt.Reason}",
            error: evt.Reason,
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
