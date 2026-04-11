using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class ClusterRegisteredConsumer(
    AuditDbContext db, ILogger<ClusterRegisteredConsumer> logger)
    : IConsumer<ClusterRegisteredEvent>
{
    public async Task Consume(ConsumeContext<ClusterRegisteredEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "cluster.registered",
            Code = EventCodes.ClusterRegistered,
            Category = "clusters",
            Severity = Severity.Info,
            Success = true,
            ResourceType = "Cluster",
            ResourceId = evt.ClusterId,
            ResourceName = evt.Name,
            ClusterId = evt.ClusterId,
            ClusterName = evt.Name,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Cluster '{evt.Name}' registered",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class ClusterConnectedConsumer(
    AuditDbContext db, ILogger<ClusterConnectedConsumer> logger)
    : IConsumer<ClusterConnectedEvent>
{
    public async Task Consume(ConsumeContext<ClusterConnectedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "cluster.connected",
            Code = EventCodes.ClusterConnected,
            Category = "clusters",
            Severity = Severity.Info,
            Success = true,
            ResourceType = "Cluster",
            ResourceId = evt.ClusterId,
            ClusterId = evt.ClusterId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Cluster {evt.ClusterId} connected (k8s {evt.KubernetesVersion ?? "unknown"})",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class ClusterDisconnectedConsumer(
    AuditDbContext db, ILogger<ClusterDisconnectedConsumer> logger)
    : IConsumer<ClusterDisconnectedEvent>
{
    public async Task Consume(ConsumeContext<ClusterDisconnectedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "cluster.disconnected",
            Code = EventCodes.ClusterDisconnected,
            Category = "clusters",
            Severity = Severity.Warning,
            Success = true,
            ResourceType = "Cluster",
            ResourceId = evt.ClusterId,
            ClusterId = evt.ClusterId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Cluster {evt.ClusterId} disconnected",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}

public sealed class ClusterDeletedConsumer(
    AuditDbContext db, ILogger<ClusterDeletedConsumer> logger)
    : IConsumer<ClusterDeletedEvent>
{
    public async Task Consume(ConsumeContext<ClusterDeletedEvent> context)
    {
        var evt = context.Message;
        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "cluster.deleted",
            Code = EventCodes.ClusterDeleted,
            Category = "clusters",
            Severity = Severity.Info,
            Success = true,
            ResourceType = "Cluster",
            ResourceId = evt.ClusterId,
            ClusterId = evt.ClusterId,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Cluster {evt.ClusterId} deleted",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
