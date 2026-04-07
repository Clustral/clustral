namespace Clustral.ControlPlane.Domain.Events;

public sealed record AccessRequestCreated(
    Guid RequestId, Guid RequesterId, Guid RoleId, Guid ClusterId,
    string? Reason, TimeSpan RequestedDuration) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AccessRequestApproved(
    Guid RequestId, Guid ReviewerId, TimeSpan GrantDuration,
    DateTimeOffset GrantExpiresAt) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AccessRequestDenied(
    Guid RequestId, Guid ReviewerId, string Reason) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AccessRequestRevoked(
    Guid RequestId, Guid RevokedById, string? Reason) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AccessRequestExpired(Guid RequestId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
