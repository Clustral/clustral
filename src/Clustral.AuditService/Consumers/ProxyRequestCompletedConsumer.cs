using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class ProxyRequestCompletedConsumer(
    IAuditEventRepository repository,
    ILogger<ProxyRequestCompletedConsumer> logger)
    : IConsumer<ProxyRequestCompletedEvent>
{
    public async Task Consume(ConsumeContext<ProxyRequestCompletedEvent> context)
    {
        var evt = context.Message;
        var severity = evt.StatusCode >= 400 ? Severity.Warning : Severity.Info;
        var success = evt.StatusCode < 400;

        var auditEvent = AuditEvent.Create(
            @event: "proxy.request",
            code: EventCodes.ProxyRequestCompleted,
            category: "proxy",
            severity: severity,
            success: success,
            time: evt.OccurredAt,
            user: evt.UserEmail,
            userId: evt.UserId,
            resourceType: "Credential",
            resourceId: evt.CredentialId,
            clusterId: evt.ClusterId,
            clusterName: evt.ClusterName,
            message: $"{evt.Method} {evt.Path} → {evt.StatusCode} ({evt.DurationMs:F0}ms)",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
