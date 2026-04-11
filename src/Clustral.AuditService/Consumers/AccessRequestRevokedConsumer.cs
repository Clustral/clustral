using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestRevokedConsumer(
    IAuditEventRepository repository,
    ILogger<AccessRequestRevokedConsumer> logger)
    : IConsumer<AccessRequestRevokedEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestRevokedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "access_request.revoked",
            code: EventCodes.AccessRequestRevoked,
            category: "access_requests",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            user: evt.RevokedByEmail,
            userId: evt.RevokedById,
            resourceType: "AccessRequest",
            resourceId: evt.RequestId,
            clusterId: evt.ClusterId != Guid.Empty ? evt.ClusterId : null,
            clusterName: evt.ClusterName,
            message: $"Access request {evt.RequestId} revoked{(evt.Reason is not null ? $": {evt.Reason}" : "")}",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
