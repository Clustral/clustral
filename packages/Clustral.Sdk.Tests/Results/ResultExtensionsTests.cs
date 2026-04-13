using Clustral.Sdk.Results;
using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Results;

public class ResultExtensionsTests(ITestOutputHelper output)
{
    [Fact]
    public void ToActionResult_Success_Returns200()
    {
        var result = Result<string>.Success("hello");
        var actionResult = result.ToActionResult();

        output.WriteLine($"Type: {actionResult.GetType().Name}");

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal("hello", ok.Value);
    }

    [Theory]
    [InlineData(ResultErrorKind.NotFound, 404)]
    [InlineData(ResultErrorKind.Unauthorized, 401)]
    [InlineData(ResultErrorKind.Forbidden, 403)]
    [InlineData(ResultErrorKind.Conflict, 409)]
    [InlineData(ResultErrorKind.BadRequest, 400)]
    [InlineData(ResultErrorKind.Validation, 422)]
    [InlineData(ResultErrorKind.Internal, 500)]
    public void ToActionResult_Failure_ReturnsCorrectStatusCode(ResultErrorKind kind, int expectedStatus)
    {
        var error = new ResultError
        {
            Kind = kind,
            Code = "TEST",
            Message = "Test error.",
        };
        var result = Result<string>.Fail(error);
        var actionResult = result.ToActionResult();

        var objectResult = Assert.IsType<ObjectResult>(actionResult);

        output.WriteLine($"{kind} => HTTP {objectResult.StatusCode}");

        Assert.Equal(expectedStatus, objectResult.StatusCode);
    }

    [Fact]
    public void ToActionResult_Failure_ReturnsProblemDetails()
    {
        var error = ResultError.NotFound("ITEM_NF", "Item not found.", "itemId");
        var result = Result<string>.Fail(error);
        var actionResult = result.ToActionResult();

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);

        output.WriteLine($"=== Problem Details ===");
        output.WriteLine($"  Type:   {problem.Type}");
        output.WriteLine($"  Title:  {problem.Title}");
        output.WriteLine($"  Status: {problem.Status}");
        output.WriteLine($"  Detail: {problem.Detail}");
        output.WriteLine($"  Field:  {problem.Extensions["field"]}");
        output.WriteLine($"  Code:   {problem.Extensions["code"]}");

        Assert.Equal("https://docs.clustral.kube.it.com/errors/item-nf", problem.Type);
        Assert.Equal("NotFound", problem.Title);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Item not found.", problem.Detail);
        Assert.Equal("itemId", problem.Extensions["field"]);
    }

    [Fact]
    public void ToActionResult_NonGeneric_Success_Returns204()
    {
        var result = Result.Success();
        var actionResult = result.ToActionResult();

        output.WriteLine($"Type: {actionResult.GetType().Name}");

        Assert.IsType<NoContentResult>(actionResult);
    }

    [Fact]
    public void ToCreatedResult_Success_Returns201()
    {
        var result = Result<object>.Success(new { id = "abc" });
        var actionResult = result.ToCreatedResult("Get", new { id = "abc" });

        output.WriteLine($"Type: {actionResult.GetType().Name}");

        var created = Assert.IsType<CreatedAtActionResult>(actionResult);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public void ToHttpStatusCode_AllKindsMapped()
    {
        foreach (var kind in Enum.GetValues<ResultErrorKind>())
        {
            var status = kind.ToHttpStatusCode();
            output.WriteLine($"{kind} => HTTP {status}");
            Assert.True(status >= 400 && status <= 599);
        }
    }

    [Fact]
    public void ProblemDetails_IncludesMetadata()
    {
        var error = ResultError.Conflict("DUP", "Duplicate.",
            new Dictionary<string, object> { ["existingId"] = "xyz" });
        var result = Result<string>.Fail(error);
        var actionResult = result.ToActionResult();

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);

        output.WriteLine($"Metadata 'existingId': {problem.Extensions["existingId"]}");

        Assert.Equal("xyz", problem.Extensions["existingId"]);
    }
}
