using Clustral.ControlPlane.Api;
using Clustral.Sdk.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Api;

public class GlobalExceptionHandlerTests(ITestOutputHelper output)
{
    private static (int StatusCode, ProblemDetails Problem) Classify(Exception ex)
    {
        var request = new DefaultHttpContext().Request;
        request.Method = "GET";
        request.Path = "/api/v1/test";
        return GlobalExceptionHandlerMiddleware.ClassifyException(ex, request, "trace-123");
    }

    [Fact]
    public void UnhandledException_Returns500()
    {
        var (status, problem) = Classify(new Exception("boom"));

        output.WriteLine($"Status: {status}");
        output.WriteLine($"Title:  {problem.Title}");
        output.WriteLine($"Detail: {problem.Detail}");

        Assert.Equal(500, status);
        Assert.Equal("Internal Server Error", problem.Title);
        Assert.Equal("An internal error occurred. Check server logs for details.", problem.Detail);
    }

    [Fact]
    public void ResultFailureException_NotFound_Returns404()
    {
        var error = ResultError.NotFound("ITEM_NF", "Item not found.", "itemId");
        var (status, problem) = Classify(new ResultFailureException(error));

        output.WriteLine($"Status: {status}, Code: {problem.Extensions["code"]}");

        Assert.Equal(404, status);
        Assert.Equal("urn:clustral:error:item_nf", problem.Type);
        Assert.Equal("Item not found.", problem.Detail);
        Assert.Equal("ITEM_NF", problem.Extensions["code"]);
        Assert.Equal("itemId", problem.Extensions["field"]);
    }

    [Fact]
    public void ResultFailureException_Conflict_Returns409()
    {
        var error = ResultError.Conflict("DUP", "Duplicate name.");
        var (status, _) = Classify(new ResultFailureException(error));

        output.WriteLine($"Status: {status}");

        Assert.Equal(409, status);
    }

    [Fact]
    public void ResultFailureException_BadRequest_Returns400()
    {
        var error = ResultError.BadRequest("BAD", "Invalid input.");
        var (status, _) = Classify(new ResultFailureException(error));

        Assert.Equal(400, status);
    }

    [Fact]
    public void ResultFailureException_Unauthorized_Returns401()
    {
        var error = ResultError.Unauthorized("Not logged in.");
        var (status, _) = Classify(new ResultFailureException(error));

        Assert.Equal(401, status);
    }

    [Fact]
    public void ResultFailureException_Forbidden_Returns403()
    {
        var error = ResultError.Forbidden("Access denied.");
        var (status, _) = Classify(new ResultFailureException(error));

        Assert.Equal(403, status);
    }

    [Fact]
    public void OperationCanceledException_Returns499()
    {
        var (status, problem) = Classify(new OperationCanceledException());

        output.WriteLine($"Status: {status}, Title: {problem.Title}");

        Assert.Equal(499, status);
        Assert.Equal("Client Closed Request", problem.Title);
    }

    [Fact]
    public void ArgumentException_Returns400()
    {
        var (status, _) = Classify(new ArgumentException("bad arg"));

        Assert.Equal(400, status);
    }

    [Fact]
    public void TimeoutException_Returns504()
    {
        var (status, problem) = Classify(new TimeoutException("timed out"));

        output.WriteLine($"Status: {status}, Title: {problem.Title}");

        Assert.Equal(504, status);
        Assert.Equal("Gateway Timeout", problem.Title);
    }

    [Fact]
    public void InvalidOperationException_Returns422()
    {
        var (status, _) = Classify(new InvalidOperationException("invalid state"));

        Assert.Equal(422, status);
    }

    [Fact]
    public void TraceId_IncludedInResponse()
    {
        var (_, problem) = Classify(new Exception("test"));

        output.WriteLine($"TraceId: {problem.Extensions["traceId"]}");

        Assert.Equal("trace-123", problem.Extensions["traceId"]);
    }

    [Fact]
    public void ProblemDetails_HasTypeUri()
    {
        var (_, problem) = Classify(new Exception("test"));

        output.WriteLine($"Type: {problem.Type}");

        Assert.StartsWith("urn:clustral:error:", problem.Type);
    }

    [Fact]
    public void InternalError_DoesNotLeakExceptionMessage()
    {
        var (_, problem) = Classify(new NullReferenceException("secret internal detail"));

        output.WriteLine($"Detail: {problem.Detail}");

        Assert.DoesNotContain("secret internal detail", problem.Detail);
        Assert.Contains("internal error", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResultFailure_WithMetadata_IncludedInProblem()
    {
        var error = ResultError.Conflict("DUP", "Exists.",
            new Dictionary<string, object> { ["existingId"] = "abc-123" });
        var (_, problem) = Classify(new ResultFailureException(error));

        output.WriteLine($"Metadata existingId: {problem.Extensions["existingId"]}");

        Assert.Equal("abc-123", problem.Extensions["existingId"]);
    }
}
