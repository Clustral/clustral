namespace Clustral.ControlPlane.Domain.Events;

public sealed record AgentAuthFailed(
    Guid? ClusterId, string Reason,
    string? CertCN = null, string? RemoteIp = null) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
