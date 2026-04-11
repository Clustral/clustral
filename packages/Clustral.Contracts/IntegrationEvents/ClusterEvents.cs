namespace Clustral.Contracts.IntegrationEvents;

public record ClusterRegisteredEvent
{
    public Guid ClusterId { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
}

public record ClusterConnectedEvent
{
    public Guid ClusterId { get; init; }
    public string? KubernetesVersion { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public record ClusterDisconnectedEvent
{
    public Guid ClusterId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public record ClusterDeletedEvent
{
    public Guid ClusterId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
