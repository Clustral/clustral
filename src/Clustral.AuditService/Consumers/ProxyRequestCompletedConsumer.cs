using MongoDB.Bson;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using MassTransit;

namespace Clustral.AuditService.Consumers;

public sealed class ProxyRequestCompletedConsumer(
    AuditDbContext db,
    ILogger<ProxyRequestCompletedConsumer> logger)
    : IConsumer<ProxyRequestCompletedEvent>
{
    public async Task Consume(ConsumeContext<ProxyRequestCompletedEvent> context)
    {
        var evt = context.Message;
        var severity = evt.StatusCode >= 400 ? Severity.Warning : Severity.Info;
        var success = evt.StatusCode < 400;

        var auditEvent = new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "proxy.request",
            Code = EventCodes.ProxyRequestCompleted,
            Category = "proxy",
            Severity = severity,
            Success = success,
            User = evt.UserEmail,
            UserId = evt.UserId,
            ResourceType = "Credential",
            ResourceId = evt.CredentialId,
            ClusterId = evt.ClusterId,
            ClusterName = evt.ClusterName,
            Time = evt.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"{evt.Method} {evt.Path} → {evt.StatusCode} ({evt.DurationMs:F0}ms)",
            Metadata = evt.ToBsonDocument(),
        };
        await db.AuditEvents.InsertOneAsync(auditEvent);
        logger.LogInformation("Audit [{Code}] {Event}: {Message}",
            auditEvent.Code, auditEvent.Event, auditEvent.Message);
    }
}
