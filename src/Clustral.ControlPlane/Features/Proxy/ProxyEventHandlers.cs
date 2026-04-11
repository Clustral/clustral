using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Infrastructure;
using MassTransit;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Proxy;

public sealed class ProxyAuditHandler(
    ILogger<ProxyAuditHandler> logger,
    IPublishEndpoint publisher,
    ClustralDb db)
    : INotificationHandler<ProxyRequestCompleted>
{
    public async Task Handle(ProxyRequestCompleted e, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == e.UserId).FirstOrDefaultAsync(ct);
        var cluster = await db.Clusters.Find(c => c.Id == e.ClusterId).FirstOrDefaultAsync(ct);

        logger.LogInformation(
            "[Audit] Proxy {Method} cluster={ClusterName} path={Path} → {StatusCode} in {Duration}ms " +
            "[user={UserEmail} credential={CredentialId}]",
            e.Method, cluster?.Name ?? e.ClusterId.ToString(), e.Path, e.StatusCode, e.DurationMs,
            user?.Email ?? e.UserId.ToString(), e.CredentialId);

        await publisher.Publish(new ProxyRequestCompletedEvent
        {
            ClusterId = e.ClusterId,
            ClusterName = cluster?.Name,
            UserId = e.UserId,
            UserEmail = user?.Email,
            CredentialId = e.CredentialId,
            Method = e.Method,
            Path = e.Path,
            StatusCode = e.StatusCode,
            DurationMs = e.DurationMs,
            RequestBody = e.RequestBody,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
