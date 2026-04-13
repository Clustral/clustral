using Clustral.Sdk.Http;
using Microsoft.AspNetCore.Mvc;

namespace Clustral.Sdk.Results;

/// <summary>
/// Maps <see cref="Result{T}"/> to ASP.NET Core <see cref="IActionResult"/>
/// using RFC 7807 Problem Details for error responses.
/// </summary>
public static class ResultExtensions
{
    /// <summary>Maps a Result to 200 OK on success, or the appropriate error status.</summary>
    public static IActionResult ToActionResult<T>(this Result<T> result) =>
        result.Match<IActionResult>(
            value => new OkObjectResult(value),
            error => ToProblemResult(error));

    /// <summary>Maps a ResultError directly to an error response.</summary>
    public static IActionResult ToActionResult(this ResultError error) =>
        ToProblemResult(error);

    /// <summary>Maps a void Result to 204 No Content on success.</summary>
    public static IActionResult ToActionResult(this Result result) =>
        result.Match<IActionResult>(
            () => new NoContentResult(),
            error => ToProblemResult(error));

    /// <summary>Maps a Result to 201 Created on success.</summary>
    public static IActionResult ToCreatedResult<T>(
        this Result<T> result,
        string actionName,
        object? routeValues = null) =>
        result.Match<IActionResult>(
            value => new CreatedAtActionResult(actionName, null, routeValues, value),
            error => ToProblemResult(error));

    /// <summary>
    /// Maps a Result error to an RpcException. Returns the value on success.
    /// Use in gRPC service implementations.
    /// </summary>
    public static T ToGrpcResult<T>(this Result<T> result)
    {
        if (result.IsSuccess) return result.Value;
        throw result.Error!.ToRpcException();
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Maps <see cref="ResultErrorKind"/> to an HTTP status code.</summary>
    public static int ToHttpStatusCode(this ResultErrorKind kind) => kind switch
    {
        ResultErrorKind.NotFound     => 404,
        ResultErrorKind.Unauthorized => 401,
        ResultErrorKind.Forbidden    => 403,
        ResultErrorKind.Conflict     => 409,
        ResultErrorKind.BadRequest   => 400,
        ResultErrorKind.Validation   => 422,
        ResultErrorKind.Internal     => 500,
        _                            => 500,
    };

    private static ObjectResult ToProblemResult(ResultError error)
    {
        // Delegate body construction to ProblemDetailsWriter so MVC actions
        // and direct-write middleware produce byte-identical RFC 7807 bodies.
        var problem = ProblemDetailsWriter.BuildProblem(error);
        return new ObjectResult(problem)
        {
            StatusCode = error.Kind.ToHttpStatusCode(),
            ContentTypes = { ProblemDetailsWriter.ProblemJsonContentType },
        };
    }

    private static global::Grpc.Core.RpcException ToRpcException(this ResultError error)
    {
        var status = error.Kind switch
        {
            ResultErrorKind.NotFound     => global::Grpc.Core.StatusCode.NotFound,
            ResultErrorKind.Unauthorized => global::Grpc.Core.StatusCode.Unauthenticated,
            ResultErrorKind.Forbidden    => global::Grpc.Core.StatusCode.PermissionDenied,
            ResultErrorKind.Conflict     => global::Grpc.Core.StatusCode.AlreadyExists,
            ResultErrorKind.BadRequest   => global::Grpc.Core.StatusCode.InvalidArgument,
            ResultErrorKind.Validation   => global::Grpc.Core.StatusCode.InvalidArgument,
            _                            => global::Grpc.Core.StatusCode.Internal,
        };
        return new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(status, error.Message));
    }
}
