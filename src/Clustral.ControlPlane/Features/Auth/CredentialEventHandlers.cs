using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Infrastructure;
using MassTransit;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Auth;

public sealed class CredentialAuditHandler(
    ILogger<CredentialAuditHandler> logger,
    IPublishEndpoint publisher,
    ClustralDb db)
    : INotificationHandler<CredentialIssued>,
      INotificationHandler<CredentialRevoked>,
      INotificationHandler<CredentialRevokeDenied>,
      INotificationHandler<CredentialIssueFailed>
{
    public async Task Handle(CredentialIssued e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Credential {CredentialId} issued for user {UserId} on cluster {ClusterId}, expires {ExpiresAt}",
            e.CredentialId, e.UserId, e.ClusterId, e.ExpiresAt);

        var user = await db.Users.Find(u => u.Id == e.UserId).FirstOrDefaultAsync(ct);
        var cluster = await db.Clusters.Find(c => c.Id == e.ClusterId).FirstOrDefaultAsync(ct);

        await publisher.Publish(new CredentialIssuedEvent
        {
            CredentialId = e.CredentialId,
            UserId = e.UserId,
            UserEmail = user?.Email,
            ClusterId = e.ClusterId,
            ClusterName = cluster?.Name,
            ExpiresAt = e.ExpiresAt,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(CredentialRevoked e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Credential {CredentialId} revoked. Reason: {Reason}",
            e.CredentialId, e.Reason ?? "(none)");

        var credential = await db.AccessTokens.Find(t => t.Id == e.CredentialId).FirstOrDefaultAsync(ct);
        var user = credential?.UserId is not null
            ? await db.Users.Find(u => u.Id == credential.UserId).FirstOrDefaultAsync(ct)
            : null;
        var cluster = credential is not null
            ? await db.Clusters.Find(c => c.Id == credential.ClusterId).FirstOrDefaultAsync(ct)
            : null;

        await publisher.Publish(new CredentialRevokedEvent
        {
            CredentialId = e.CredentialId,
            UserId = credential?.UserId ?? Guid.Empty,
            UserEmail = user?.Email,
            ClusterId = credential?.ClusterId ?? default,
            ClusterName = cluster?.Name,
            RevokedByEmail = e.ActorEmail,
            Reason = e.Reason,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(CredentialRevokeDenied e, CancellationToken ct)
    {
        logger.LogWarning("[Audit] Credential revoke denied: {Reason}", e.Reason);

        await publisher.Publish(new CredentialRevokeDeniedEvent
        {
            CredentialId = e.CredentialId,
            Reason = e.Reason,
            ActorEmail = e.ActorEmail,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(CredentialIssueFailed e, CancellationToken ct)
    {
        var cluster = await db.Clusters.Find(c => c.Id == e.ClusterId).FirstOrDefaultAsync(ct);

        logger.LogWarning("[Audit] Credential issuance failed for cluster {ClusterId}: {Reason}",
            e.ClusterId, e.Reason);

        await publisher.Publish(new CredentialIssueFailedEvent
        {
            ClusterId = e.ClusterId,
            ClusterName = cluster?.Name,
            Reason = e.Reason,
            ActorEmail = e.ActorEmail,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
