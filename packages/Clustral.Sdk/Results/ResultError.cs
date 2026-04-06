namespace Clustral.Sdk.Results;

/// <summary>
/// Describes a failure in a <see cref="Result{T}"/> with a machine-readable
/// code, human-readable message, and optional field/metadata context.
/// </summary>
public sealed record ResultError
{
    public required ResultErrorKind Kind { get; init; }

    /// <summary>Machine-readable error code, e.g. <c>ROLE_NOT_FOUND</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable error message.</summary>
    public required string Message { get; init; }

    /// <summary>Optional field name that caused the error (for validation errors).</summary>
    public string? Field { get; init; }

    /// <summary>Optional correlation/trace ID for debugging.</summary>
    public string? TraceId { get; init; }

    /// <summary>Optional extra context (e.g., conflicting resource ID).</summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    // ── Convenience factories ────────────────────────────────────────────

    public static ResultError NotFound(string code, string message, string? field = null) =>
        new() { Kind = ResultErrorKind.NotFound, Code = code, Message = message, Field = field };

    public static ResultError Unauthorized(string message) =>
        new() { Kind = ResultErrorKind.Unauthorized, Code = "UNAUTHORIZED", Message = message };

    public static ResultError Forbidden(string message) =>
        new() { Kind = ResultErrorKind.Forbidden, Code = "FORBIDDEN", Message = message };

    public static ResultError Conflict(string code, string message, IReadOnlyDictionary<string, object>? metadata = null) =>
        new() { Kind = ResultErrorKind.Conflict, Code = code, Message = message, Metadata = metadata };

    public static ResultError BadRequest(string code, string message, string? field = null) =>
        new() { Kind = ResultErrorKind.BadRequest, Code = code, Message = message, Field = field };

    public static ResultError Validation(string message, string field) =>
        new() { Kind = ResultErrorKind.Validation, Code = "VALIDATION_ERROR", Message = message, Field = field };

    public static ResultError Internal(string message) =>
        new() { Kind = ResultErrorKind.Internal, Code = "INTERNAL_ERROR", Message = message };
}

/// <summary>
/// Categorizes a <see cref="ResultError"/> for HTTP/gRPC status mapping.
/// </summary>
public enum ResultErrorKind
{
    NotFound,
    Unauthorized,
    Forbidden,
    Conflict,
    BadRequest,
    Validation,
    Internal,
}
