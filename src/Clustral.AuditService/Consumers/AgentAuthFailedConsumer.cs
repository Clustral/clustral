using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AgentAuthFailedConsumer(
    IAuditEventRepository repository,
    ILogger<AgentAuthFailedConsumer> logger)
    : IConsumer<AgentAuthFailedEvent>
{
    public async Task Consume(ConsumeContext<AgentAuthFailedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "agent.auth_failed",
            code: EventCodes.AgentAuthFailed,
            category: "auth",
            severity: Severity.Warning,
            success: false,
            time: evt.OccurredAt,
            resourceType: "Cluster",
            resourceId: evt.ClusterId,
            clusterId: evt.ClusterId,
            clusterName: evt.ClusterName,
            clientIp: evt.RemoteIp,
            message: $"Agent auth failed{(evt.CertCN is not null ? $" (CN={evt.CertCN})" : "")}: {evt.Reason}",
            error: evt.Reason,
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
