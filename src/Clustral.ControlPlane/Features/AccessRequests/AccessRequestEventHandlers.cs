using Clustral.ControlPlane.Domain.Events;
using MediatR;

namespace Clustral.ControlPlane.Features.AccessRequests;

/// <summary>
/// Handles access request domain events for audit logging.
/// Additional handlers (notifications, webhooks) can be added as
/// separate INotificationHandler implementations.
/// </summary>
public sealed class AccessRequestAuditHandler(ILogger<AccessRequestAuditHandler> logger)
    : INotificationHandler<AccessRequestCreated>,
      INotificationHandler<AccessRequestApproved>,
      INotificationHandler<AccessRequestDenied>,
      INotificationHandler<AccessRequestRevoked>
{
    public Task Handle(AccessRequestCreated e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} created by user {RequesterId} " +
            "for role {RoleId} on cluster {ClusterId}. Reason: {Reason}",
            e.RequestId, e.RequesterId, e.RoleId, e.ClusterId, e.Reason ?? "(none)");
        return Task.CompletedTask;
    }

    public Task Handle(AccessRequestApproved e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} approved by {ReviewerId}. " +
            "Grant duration: {Duration}, expires: {ExpiresAt}",
            e.RequestId, e.ReviewerId, e.GrantDuration, e.GrantExpiresAt);
        return Task.CompletedTask;
    }

    public Task Handle(AccessRequestDenied e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} denied by {ReviewerId}. Reason: {Reason}",
            e.RequestId, e.ReviewerId, e.Reason);
        return Task.CompletedTask;
    }

    public Task Handle(AccessRequestRevoked e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Access request {RequestId} revoked by {RevokedById}. Reason: {Reason}",
            e.RequestId, e.RevokedById, e.Reason ?? "(none)");
        return Task.CompletedTask;
    }
}
