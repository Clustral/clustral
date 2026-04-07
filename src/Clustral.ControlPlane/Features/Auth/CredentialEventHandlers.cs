using Clustral.ControlPlane.Domain.Events;
using MediatR;

namespace Clustral.ControlPlane.Features.Auth;

public sealed class CredentialAuditHandler(ILogger<CredentialAuditHandler> logger)
    : INotificationHandler<CredentialIssued>,
      INotificationHandler<CredentialRevoked>
{
    public Task Handle(CredentialIssued e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Credential {CredentialId} issued for user {UserId} on cluster {ClusterId}, expires {ExpiresAt}",
            e.CredentialId, e.UserId, e.ClusterId, e.ExpiresAt);
        return Task.CompletedTask;
    }

    public Task Handle(CredentialRevoked e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Credential {CredentialId} revoked. Reason: {Reason}",
            e.CredentialId, e.Reason ?? "(none)");
        return Task.CompletedTask;
    }
}
