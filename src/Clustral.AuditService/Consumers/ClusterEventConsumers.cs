using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class ClusterRegisteredConsumer(
    IAuditEventRepository repository, ILogger<ClusterRegisteredConsumer> logger)
    : IConsumer<ClusterRegisteredEvent>
{
    public async Task Consume(ConsumeContext<ClusterRegisteredEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "cluster.registered",
            code: EventCodes.ClusterRegistered,
            category: "clusters",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            resourceType: "Cluster",
            resourceId: evt.ClusterId,
            resourceName: evt.Name,
            clusterId: evt.ClusterId,
            clusterName: evt.Name,
            message: $"Cluster '{evt.Name}' registered",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class ClusterConnectedConsumer(
    IAuditEventRepository repository, ILogger<ClusterConnectedConsumer> logger)
    : IConsumer<ClusterConnectedEvent>
{
    public async Task Consume(ConsumeContext<ClusterConnectedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "cluster.connected",
            code: EventCodes.ClusterConnected,
            category: "clusters",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            resourceType: "Cluster",
            resourceId: evt.ClusterId,
            clusterId: evt.ClusterId,
            message: $"Cluster {evt.ClusterId} connected (k8s {evt.KubernetesVersion ?? "unknown"})",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class ClusterDisconnectedConsumer(
    IAuditEventRepository repository, ILogger<ClusterDisconnectedConsumer> logger)
    : IConsumer<ClusterDisconnectedEvent>
{
    public async Task Consume(ConsumeContext<ClusterDisconnectedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "cluster.disconnected",
            code: EventCodes.ClusterDisconnected,
            category: "clusters",
            severity: Severity.Warning,
            success: true,
            time: evt.OccurredAt,
            resourceType: "Cluster",
            resourceId: evt.ClusterId,
            clusterId: evt.ClusterId,
            message: $"Cluster {evt.ClusterId} disconnected",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class ClusterDeletedConsumer(
    IAuditEventRepository repository, ILogger<ClusterDeletedConsumer> logger)
    : IConsumer<ClusterDeletedEvent>
{
    public async Task Consume(ConsumeContext<ClusterDeletedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = AuditEvent.Create(
            @event: "cluster.deleted",
            code: EventCodes.ClusterDeleted,
            category: "clusters",
            severity: Severity.Info,
            success: true,
            time: evt.OccurredAt,
            resourceType: "Cluster",
            resourceId: evt.ClusterId,
            clusterId: evt.ClusterId,
            message: $"Cluster {evt.ClusterId} deleted",
            metadata: evt.ToBsonDocument());
        await repository.InsertAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
