using Clustral.ControlPlane.Domain.Events;
using MediatR;

namespace Clustral.ControlPlane.Features.Shared;

public sealed class ProxyAuditHandler(ILogger<ProxyAuditHandler> logger)
    : INotificationHandler<ProxyRequestCompleted>
{
    public Task Handle(ProxyRequestCompleted e, CancellationToken ct)
    {
        logger.LogInformation(
            "[Audit] Proxy {Method} cluster={ClusterId} path={Path} → {StatusCode} in {Duration}ms " +
            "[user={UserId} credential={CredentialId}]",
            e.Method, e.ClusterId, e.Path, e.StatusCode, e.DurationMs,
            e.UserId, e.CredentialId);
        return Task.CompletedTask;
    }
}
