namespace Clustral.Contracts.IntegrationEvents;

public record UserSyncedEvent
{
    public Guid UserId { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string? Email { get; init; }
    public bool IsNew { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public record RoleAssignedEvent
{
    public Guid UserId { get; init; }
    public string? UserEmail { get; init; }
    public Guid RoleId { get; init; }
    public string? RoleName { get; init; }
    public Guid ClusterId { get; init; }
    public string? ClusterName { get; init; }
    public string AssignedBy { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
}

public record RoleUnassignedEvent
{
    public Guid AssignmentId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
