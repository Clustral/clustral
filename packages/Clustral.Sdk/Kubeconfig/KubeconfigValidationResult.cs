namespace Clustral.Sdk.Kubeconfig;

/// <summary>
/// Result of validating a kubeconfig document for structural integrity
/// and optional security policy compliance.
/// </summary>
public sealed record KubeconfigValidationResult(
    bool IsValid,
    IReadOnlyList<KubeconfigValidationError> Errors)
{
    public static KubeconfigValidationResult Success { get; } =
        new(true, Array.Empty<KubeconfigValidationError>());
}

/// <summary>
/// A single validation error or warning with enough context to be actionable.
/// </summary>
public sealed record KubeconfigValidationError(
    KubeconfigValidationSeverity Severity,
    string Section,
    string EntityName,
    string Field,
    string Code,
    string Message);

public enum KubeconfigValidationSeverity
{
    Error,
    Warning,
}
