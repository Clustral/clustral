namespace Clustral.Contracts.IntegrationEvents;

public record AgentAuthFailedEvent
{
    public Guid? ClusterId { get; init; }
    public string? ClusterName { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? CertCN { get; init; }
    public string? RemoteIp { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
