namespace Clustral.Contracts.IntegrationEvents;

public record CredentialIssuedEvent
{
    public Guid CredentialId { get; init; }
    public Guid UserId { get; init; }
    public string? UserEmail { get; init; }
    public Guid ClusterId { get; init; }
    public string? ClusterName { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public record CredentialRevokedEvent
{
    public Guid CredentialId { get; init; }
    public Guid UserId { get; init; }
    public string? UserEmail { get; init; }
    public Guid ClusterId { get; init; }
    public string? ClusterName { get; init; }
    public string? RevokedByEmail { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
