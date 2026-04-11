using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class ProxyAccessDeniedConsumer(
    IAuditEventRepository repository,
    ILogger<ProxyAccessDeniedConsumer> logger)
    : IConsumer<ProxyAccessDeniedEvent>
{
    public async Task Consume(ConsumeContext<ProxyAccessDeniedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "proxy.access_denied",
            code: EventCodes.ProxyAccessDenied,
            category: "proxy",
            severity: Severity.Warning,
            success: false,
            time: evt.OccurredAt,
            user: evt.UserEmail,
            userId: evt.UserId,
            resourceType: "Cluster",
            resourceId: evt.ClusterId,
            clusterId: evt.ClusterId,
            clusterName: evt.ClusterName,
            message: $"{evt.Method} {evt.Path} denied: {evt.Reason}",
            error: evt.Reason,
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
