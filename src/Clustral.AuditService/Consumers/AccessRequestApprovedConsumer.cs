using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class AccessRequestApprovedConsumer(
    IAuditEventRepository repository,
    ILogger<AccessRequestApprovedConsumer> logger)
    : IConsumer<AccessRequestApprovedEvent>
{
    public async Task Consume(ConsumeContext<AccessRequestApprovedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "access_request.approved",
            code: EventCodes.AccessRequestApproved,
            category: "access_requests",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            user: evt.ReviewerEmail,
            userId: evt.ReviewerId,
            resourceType: "AccessRequest",
            resourceId: evt.RequestId,
            clusterId: evt.ClusterId,
            clusterName: evt.ClusterName,
            message: $"Access request {evt.RequestId} approved by {evt.ReviewerEmail ?? evt.ReviewerId.ToString()}, grant expires {evt.GrantExpiresAt}",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
