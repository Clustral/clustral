using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using MassTransit;
using MediatR;

namespace Clustral.ControlPlane.Features.AccessRequests;

/// <summary>
/// Handles access request domain events for audit logging.
/// Additional handlers (notifications, webhooks) can be added as
/// separate INotificationHandler implementations.
/// </summary>
public sealed class AccessRequestAuditHandler(ILogger<AccessRequestAuditHandler> logger, IPublishEndpoint publisher)
    : INotificationHandler<AccessRequestCreated>,
      INotificationHandler<AccessRequestApproved>,
      INotificationHandler<AccessRequestDenied>,
      INotificationHandler<AccessRequestRevoked>
{
    public async Task Handle(AccessRequestCreated e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} created by user {RequesterId} " +
            "for role {RoleId} on cluster {ClusterId}. Reason: {Reason}",
            e.RequestId, e.RequesterId, e.RoleId, e.ClusterId, e.Reason ?? "(none)");

        await publisher.Publish(new AccessRequestCreatedEvent
        {
            RequestId = e.RequestId,
            RequesterId = e.RequesterId,
            RequesterEmail = null,
            RoleId = e.RoleId,
            RoleName = null,
            ClusterId = e.ClusterId,
            ClusterName = null,
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

        await publisher.Publish(new AccessRequestApprovedEvent
        {
            RequestId = e.RequestId,
            ReviewerId = e.ReviewerId,
            ReviewerEmail = null,
            ClusterId = default,
            ClusterName = null,
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

        await publisher.Publish(new AccessRequestDeniedEvent
        {
            RequestId = e.RequestId,
            ReviewerId = e.ReviewerId,
            ReviewerEmail = null,
            Reason = e.Reason,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(AccessRequestRevoked e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} revoked by {RevokedById}. Reason: {Reason}",
            e.RequestId, e.RevokedById, e.Reason ?? "(none)");

        await publisher.Publish(new AccessRequestRevokedEvent
        {
            RequestId = e.RequestId,
            RevokedById = e.RevokedById,
            RevokedByEmail = null,
            Reason = e.Reason,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
