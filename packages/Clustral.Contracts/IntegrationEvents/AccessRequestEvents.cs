namespace Clustral.Contracts.IntegrationEvents;

public record AccessRequestCreatedEvent
{
    public Guid RequestId { get; init; }
    public Guid RequesterId { get; init; }
    public string? RequesterEmail { get; init; }
    public Guid RoleId { get; init; }
    public string? RoleName { get; init; }
    public Guid ClusterId { get; init; }
    public string? ClusterName { get; init; }
    public string? Reason { get; init; }
    public TimeSpan RequestedDuration { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public record AccessRequestApprovedEvent
{
    public Guid RequestId { get; init; }
    public Guid ReviewerId { get; init; }
    public string? ReviewerEmail { get; init; }
    public Guid ClusterId { get; init; }
    public string? ClusterName { get; init; }
    public TimeSpan GrantDuration { get; init; }
    public DateTimeOffset GrantExpiresAt { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public record AccessRequestDeniedEvent
{
    public Guid RequestId { get; init; }
    public Guid ReviewerId { get; init; }
    public string? ReviewerEmail { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
}

public record AccessRequestRevokedEvent
{
    public Guid RequestId { get; init; }
    public Guid RevokedById { get; init; }
    public string? RevokedByEmail { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public record AccessRequestExpiredEvent
{
    public Guid RequestId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
