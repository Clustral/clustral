namespace Clustral.Contracts.IntegrationEvents;

public record ProxyRequestCompletedEvent
{
    public Guid ClusterId { get; init; }
    public string? ClusterName { get; init; }
    public Guid UserId { get; init; }
    public string? UserEmail { get; init; }
    public Guid CredentialId { get; init; }
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public double DurationMs { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
