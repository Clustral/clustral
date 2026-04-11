namespace Clustral.ControlPlane.Domain.Events;

public sealed record CredentialIssued(
    Guid CredentialId, Guid UserId, Guid ClusterId,
    DateTimeOffset ExpiresAt) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CredentialRevoked(
    Guid CredentialId, string? Reason,
    string? ActorEmail = null) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CredentialRevokeDenied(
    Guid CredentialId, string Reason,
    string? ActorEmail = null) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CredentialIssueFailed(
    Guid ClusterId, string Reason,
    string? ActorEmail = null) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
