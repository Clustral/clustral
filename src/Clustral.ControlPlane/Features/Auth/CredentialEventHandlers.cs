using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using MassTransit;
using MediatR;

namespace Clustral.ControlPlane.Features.Auth;

public sealed class CredentialAuditHandler(ILogger<CredentialAuditHandler> logger, IPublishEndpoint publisher)
    : INotificationHandler<CredentialIssued>,
      INotificationHandler<CredentialRevoked>
{
    public async Task Handle(CredentialIssued e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Credential {CredentialId} issued for user {UserId} on cluster {ClusterId}, expires {ExpiresAt}",
            e.CredentialId, e.UserId, e.ClusterId, e.ExpiresAt);

        await publisher.Publish(new CredentialIssuedEvent
        {
            CredentialId = e.CredentialId,
            UserId = e.UserId,
            UserEmail = null,
            ClusterId = e.ClusterId,
            ClusterName = null,
            ExpiresAt = e.ExpiresAt,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(CredentialRevoked e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Credential {CredentialId} revoked. Reason: {Reason}",
            e.CredentialId, e.Reason ?? "(none)");

        await publisher.Publish(new CredentialRevokedEvent
        {
            CredentialId = e.CredentialId,
            Reason = e.Reason,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
