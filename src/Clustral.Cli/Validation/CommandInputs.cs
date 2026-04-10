namespace Clustral.Cli.Validation;

internal sealed record KubeLoginInput(string Cluster, string? Ttl);

internal sealed record AccessRequestInput(string Role, string Cluster, string? Duration);

internal sealed record AccessActionInput(string RequestId);

internal sealed record AccessDenyInput(string RequestId, string Reason);
