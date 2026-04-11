using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestCreatedConsumer(
    IAuditEventRepository repository,
    ILogger<AccessRequestCreatedConsumer> logger)
    : IConsumer<AccessRequestCreatedEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestCreatedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "access_request.created",
            code: EventCodes.AccessRequestCreated,
            category: "access_requests",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            user: evt.RequesterEmail,
            userId: evt.RequesterId,
            resourceType: "AccessRequest",
            resourceId: evt.RequestId,
            clusterId: evt.ClusterId,
            clusterName: evt.ClusterName,
            message: $"Access request {evt.RequestId} created for role {evt.RoleName ?? evt.RoleId.ToString()} on cluster {evt.ClusterName ?? evt.ClusterId.ToString()}",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
