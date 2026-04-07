namespace Clustral.ControlPlane.Domain.Events;

public sealed record ProxyRequestCompleted(
    Guid ClusterId, Guid UserId, Guid CredentialId,
    string Method, string Path, int StatusCode,
    double DurationMs) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
