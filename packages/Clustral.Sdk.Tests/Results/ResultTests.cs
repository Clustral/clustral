using Clustral.Sdk.Results;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Results;

public class ResultTests(ITestOutputHelper output)
{
    [Fact]
    public void Success_CreatesSuccessResult()
    {
        var result = Result<int>.Success(42);

        output.WriteLine($"IsSuccess: {result.IsSuccess}, Value: {result.Value}");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_CreatesFailureResult()
    {
        var error = ResultError.NotFound("TEST_NOT_FOUND", "Item not found.");
        var result = Result<int>.Fail(error);

        output.WriteLine($"IsFailure: {result.IsFailure}, Error: {result.Error!.Code}");

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal("TEST_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public void ImplicitConversion_FromValue()
    {
        Result<string> result = "hello";

        output.WriteLine($"Implicit from value: {result.Value}");

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromError()
    {
        Result<string> result = ResultError.BadRequest("BAD", "Bad input.");

        output.WriteLine($"Implicit from error: {result.Error!.Code}");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Value_ThrowsOnFailure()
    {
        var result = Result<int>.Fail(ResultError.Internal("Oops"));

        output.WriteLine("Accessing Value on failed result => InvalidOperationException");

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Map_TransformsSuccessValue()
    {
        var result = Result<int>.Success(5);
        var mapped = result.Map(v => v * 2);

        output.WriteLine($"Map(5 * 2) => {mapped.Value}");

        Assert.True(mapped.IsSuccess);
        Assert.Equal(10, mapped.Value);
    }

    [Fact]
    public void Map_PassesThroughError()
    {
        var error = ResultError.NotFound("X", "Missing.");
        var result = Result<int>.Fail(error);
        var mapped = result.Map(v => v * 2);

        output.WriteLine($"Map on failure => error passed through: {mapped.Error!.Code}");

        Assert.True(mapped.IsFailure);
        Assert.Equal("X", mapped.Error!.Code);
    }

    [Fact]
    public async Task MapAsync_TransformsSuccessValue()
    {
        var result = Result<int>.Success(3);
        var mapped = await result.MapAsync(async v =>
        {
            await Task.Delay(1);
            return v.ToString();
        });

        output.WriteLine($"MapAsync(3) => \"{mapped.Value}\"");

        Assert.True(mapped.IsSuccess);
        Assert.Equal("3", mapped.Value);
    }

    [Fact]
    public void Ensure_Passes_WhenPredicateTrue()
    {
        var result = Result<int>.Success(10)
            .Ensure(v => v > 0, ResultError.BadRequest("NEG", "Must be positive."));

        output.WriteLine($"Ensure(10 > 0) => IsSuccess: {result.IsSuccess}");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Ensure_Fails_WhenPredicateFalse()
    {
        var result = Result<int>.Success(-1)
            .Ensure(v => v > 0, ResultError.BadRequest("NEG", "Must be positive."));

        output.WriteLine($"Ensure(-1 > 0) => IsFailure: {result.IsFailure}, Code: {result.Error!.Code}");

        Assert.True(result.IsFailure);
        Assert.Equal("NEG", result.Error!.Code);
    }

    [Fact]
    public void Match_DispatchesCorrectly()
    {
        var success = Result<int>.Success(42);
        var failure = Result<int>.Fail(ResultError.Internal("err"));

        var s = success.Match(v => $"ok:{v}", e => $"err:{e.Code}");
        var f = failure.Match(v => $"ok:{v}", e => $"err:{e.Code}");

        output.WriteLine($"Match success: {s}");
        output.WriteLine($"Match failure: {f}");

        Assert.Equal("ok:42", s);
        Assert.Equal("err:INTERNAL_ERROR", f);
    }

    [Fact]
    public void OnFailure_InvokedOnError()
    {
        string? captured = null;
        var result = Result<int>.Fail(ResultError.NotFound("X", "Missing."))
            .OnFailure(e => captured = e.Code);

        output.WriteLine($"OnFailure captured: {captured}");

        Assert.Equal("X", captured);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void OnFailure_NotInvokedOnSuccess()
    {
        string? captured = null;
        Result<int>.Success(1).OnFailure(e => captured = e.Code);

        output.WriteLine($"OnFailure on success: captured = {captured ?? "null"}");

        Assert.Null(captured);
    }

    [Fact]
    public void ThrowIfFailed_ThrowsResultFailureException()
    {
        var result = Result<int>.Fail(ResultError.Conflict("DUP", "Duplicate."));

        var ex = Assert.Throws<ResultFailureException>(() => result.ThrowIfFailed());
        output.WriteLine($"ThrowIfFailed: {ex.Error.Code} — {ex.Message}");

        Assert.Equal("DUP", ex.Error.Code);
    }

    [Fact]
    public void ThrowIfFailed_ReturnsValueOnSuccess()
    {
        var result = Result<int>.Success(99);
        var value = result.ThrowIfFailed();

        output.WriteLine($"ThrowIfFailed on success: {value}");

        Assert.Equal(99, value);
    }

    // ── Non-generic Result ──────────────────────────────────────────────────

    [Fact]
    public void NonGeneric_Success()
    {
        var result = Result.Success();

        output.WriteLine($"Result.Success() => IsSuccess: {result.IsSuccess}");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void NonGeneric_Fail()
    {
        var result = Result.Fail(ResultError.Internal("boom"));

        output.WriteLine($"Result.Fail() => IsFailure: {result.IsFailure}");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ToString_ShowsState()
    {
        var success = Result<int>.Success(42);
        var failure = Result<int>.Fail(ResultError.NotFound("NF", "Not found."));

        output.WriteLine($"Success: {success}");
        output.WriteLine($"Failure: {failure}");

        Assert.Contains("Success", success.ToString());
        Assert.Contains("NF", failure.ToString());
    }
}
