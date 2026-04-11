namespace Clustral.ControlPlane.Domain.Events;

public sealed record ClusterRegistered(
    Guid ClusterId, string Name) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ClusterConnected(
    Guid ClusterId, string? KubernetesVersion) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ClusterDisconnected(Guid ClusterId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ClusterDeleted(Guid ClusterId, string? ClusterName,
    string? ActorEmail = null) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AgentCredentialsRevoked(
    Guid ClusterId, int NewTokenVersion) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
