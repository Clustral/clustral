namespace Clustral.ControlPlane.Domain.Events;

public sealed record UserSynced(
    Guid UserId, string Subject, string? Email, bool IsNew) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RoleAssigned(
    Guid UserId, Guid RoleId, Guid ClusterId, string AssignedBy) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RoleUnassigned(
    Guid AssignmentId, Guid UserId, Guid RoleId, Guid ClusterId,
    string? ActorEmail = null) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
