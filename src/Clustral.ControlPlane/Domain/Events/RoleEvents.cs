namespace Clustral.ControlPlane.Domain.Events;

public sealed record RoleCreated(
    Guid RoleId, string Name, List<string> KubernetesGroups) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RoleUpdated(
    Guid RoleId, string? Name, string? Description, List<string>? KubernetesGroups) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RoleDeleted(Guid RoleId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
