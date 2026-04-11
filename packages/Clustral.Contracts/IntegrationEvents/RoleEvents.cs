namespace Clustral.Contracts.IntegrationEvents;

public record RoleCreatedEvent
{
    public Guid RoleId { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<string> KubernetesGroups { get; init; } = [];
    public DateTimeOffset OccurredAt { get; init; }
}

public record RoleUpdatedEvent
{
    public Guid RoleId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public List<string>? KubernetesGroups { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public record RoleDeletedEvent
{
    public Guid RoleId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
