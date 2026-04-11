using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Infrastructure;
using MassTransit;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.AccessRequests;

/// <summary>
/// Handles access request domain events for audit logging.
/// Enriches integration events with user emails and cluster names by
/// looking up entities in the database — domain events carry only IDs.
/// </summary>
public sealed class AccessRequestAuditHandler(
    ILogger<AccessRequestAuditHandler> logger,
    IPublishEndpoint publisher,
    ClustralDb db)
    : INotificationHandler<AccessRequestCreated>,
      INotificationHandler<AccessRequestApproved>,
      INotificationHandler<AccessRequestDenied>,
      INotificationHandler<AccessRequestRevoked>,
      INotificationHandler<AccessRequestExpired>
{
    public async Task Handle(AccessRequestCreated e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} created by user {RequesterId} " +
            "for role {RoleId} on cluster {ClusterId}. Reason: {Reason}",
            e.RequestId, e.RequesterId, e.RoleId, e.ClusterId, e.Reason ?? "(none)");

        var user = await db.Users.Find(u => u.Id == e.RequesterId).FirstOrDefaultAsync(ct);
        var role = await db.Roles.Find(r => r.Id == e.RoleId).FirstOrDefaultAsync(ct);
        var cluster = await db.Clusters.Find(c => c.Id == e.ClusterId).FirstOrDefaultAsync(ct);

        await publisher.Publish(new AccessRequestCreatedEvent
        {
            RequestId = e.RequestId,
            RequesterId = e.RequesterId,
            RequesterEmail = user?.Email,
            RoleId = e.RoleId,
            RoleName = role?.Name,
            ClusterId = e.ClusterId,
            ClusterName = cluster?.Name,
            Reason = e.Reason,
            RequestedDuration = e.RequestedDuration,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(AccessRequestApproved e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} approved by {ReviewerId}. " +
            "Grant duration: {Duration}, expires: {ExpiresAt}",
            e.RequestId, e.ReviewerId, e.GrantDuration, e.GrantExpiresAt);

        var reviewer = await db.Users.Find(u => u.Id == e.ReviewerId).FirstOrDefaultAsync(ct);
        var ar = await db.AccessRequests.Find(r => r.Id == e.RequestId).FirstOrDefaultAsync(ct);
        var cluster = ar is not null
            ? await db.Clusters.Find(c => c.Id == ar.ClusterId).FirstOrDefaultAsync(ct)
            : null;

        await publisher.Publish(new AccessRequestApprovedEvent
        {
            RequestId = e.RequestId,
            ReviewerId = e.ReviewerId,
            ReviewerEmail = reviewer?.Email,
            ClusterId = ar?.ClusterId ?? default,
            ClusterName = cluster?.Name,
            GrantDuration = e.GrantDuration,
            GrantExpiresAt = e.GrantExpiresAt,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(AccessRequestDenied e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} denied by {ReviewerId}. Reason: {Reason}",
            e.RequestId, e.ReviewerId, e.Reason);

        var reviewer = await db.Users.Find(u => u.Id == e.ReviewerId).FirstOrDefaultAsync(ct);
        var ar = await db.AccessRequests.Find(r => r.Id == e.RequestId).FirstOrDefaultAsync(ct);
        var cluster = ar is not null
            ? await db.Clusters.Find(c => c.Id == ar.ClusterId).FirstOrDefaultAsync(ct)
            : null;

        await publisher.Publish(new AccessRequestDeniedEvent
        {
            RequestId = e.RequestId,
            ReviewerId = e.ReviewerId,
            ReviewerEmail = reviewer?.Email,
            ClusterId = ar?.ClusterId ?? default,
            ClusterName = cluster?.Name,
            Reason = e.Reason,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(AccessRequestRevoked e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} revoked by {RevokedById}. Reason: {Reason}",
            e.RequestId, e.RevokedById, e.Reason ?? "(none)");

        var revoker = await db.Users.Find(u => u.Id == e.RevokedById).FirstOrDefaultAsync(ct);
        var ar = await db.AccessRequests.Find(r => r.Id == e.RequestId).FirstOrDefaultAsync(ct);
        var cluster = ar is not null
            ? await db.Clusters.Find(c => c.Id == ar.ClusterId).FirstOrDefaultAsync(ct)
            : null;

        await publisher.Publish(new AccessRequestRevokedEvent
        {
            RequestId = e.RequestId,
            RevokedById = e.RevokedById,
            RevokedByEmail = revoker?.Email,
            ClusterId = ar?.ClusterId ?? default,
            ClusterName = cluster?.Name,
            Reason = e.Reason,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(AccessRequestExpired e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Access request {RequestId} expired", e.RequestId);

        var ar = await db.AccessRequests.Find(r => r.Id == e.RequestId).FirstOrDefaultAsync(ct);
        var requester = ar is not null
            ? await db.Users.Find(u => u.Id == ar.RequesterId).FirstOrDefaultAsync(ct)
            : null;
        var cluster = ar is not null
            ? await db.Clusters.Find(c => c.Id == ar.ClusterId).FirstOrDefaultAsync(ct)
            : null;

        await publisher.Publish(new AccessRequestExpiredEvent
        {
            RequestId = e.RequestId,
            RequesterId = ar?.RequesterId ?? default,
            RequesterEmail = requester?.Email,
            ClusterId = ar?.ClusterId ?? default,
            ClusterName = cluster?.Name,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
