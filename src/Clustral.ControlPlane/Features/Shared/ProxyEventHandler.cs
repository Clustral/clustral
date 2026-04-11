using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using MassTransit;
using MediatR;

namespace Clustral.ControlPlane.Features.Shared;

public sealed class ProxyAuditHandler(ILogger<ProxyAuditHandler> logger, IPublishEndpoint publisher)
    : INotificationHandler<ProxyRequestCompleted>
{
    public async Task Handle(ProxyRequestCompleted e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Proxy {Method} cluster={ClusterId} path={Path} → {StatusCode} in {Duration}ms " +
            "[user={UserId} credential={CredentialId}]",
            e.Method, e.ClusterId, e.Path, e.StatusCode, e.DurationMs,
            e.UserId, e.CredentialId);

        await publisher.Publish(new ProxyRequestCompletedEvent
        {
            ClusterId = e.ClusterId,
            ClusterName = null,
            UserId = e.UserId,
            UserEmail = null,
            CredentialId = e.CredentialId,
            Method = e.Method,
            Path = e.Path,
            StatusCode = e.StatusCode,
            DurationMs = e.DurationMs,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
