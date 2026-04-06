namespace Clustral.Sdk.Results;

/// <summary>
/// Bridge between the Result pattern and exception-based code paths.
/// Thrown by <see cref="Result{T}.ThrowIfFailed"/> and caught by the
/// global exception handler middleware to produce an appropriate HTTP response.
/// </summary>
public sealed class ResultFailureException : Exception
{
    public ResultError Error { get; }

    public ResultFailureException(ResultError error)
        : base(error.Message)
    {
        Error = error;
    }
}
