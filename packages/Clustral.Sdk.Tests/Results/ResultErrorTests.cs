using Clustral.Sdk.Results;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Results;

public class ResultErrorTests(ITestOutputHelper output)
{
    [Fact]
    public void NotFound_ProducesCorrectKind()
    {
        var error = ResultError.NotFound("ITEM_NOT_FOUND", "Item not found.", "itemId");

        output.WriteLine($"Kind: {error.Kind}, Code: {error.Code}, Field: {error.Field}");

        Assert.Equal(ResultErrorKind.NotFound, error.Kind);
        Assert.Equal("ITEM_NOT_FOUND", error.Code);
        Assert.Equal("itemId", error.Field);
    }

    [Fact]
    public void Unauthorized_ProducesCorrectKind()
    {
        var error = ResultError.Unauthorized("Not logged in.");

        output.WriteLine($"Kind: {error.Kind}, Code: {error.Code}");

        Assert.Equal(ResultErrorKind.Unauthorized, error.Kind);
        Assert.Equal("UNAUTHORIZED", error.Code);
    }

    [Fact]
    public void Forbidden_ProducesCorrectKind()
    {
        var error = ResultError.Forbidden("Access denied.");
        Assert.Equal(ResultErrorKind.Forbidden, error.Kind);
    }

    [Fact]
    public void Conflict_WithMetadata()
    {
        var error = ResultError.Conflict("DUP", "Duplicate.",
            new Dictionary<string, object> { ["existingId"] = "abc-123" });

        output.WriteLine($"Kind: {error.Kind}, Metadata: existingId={error.Metadata!["existingId"]}");

        Assert.Equal(ResultErrorKind.Conflict, error.Kind);
        Assert.Equal("abc-123", error.Metadata!["existingId"]);
    }

    [Fact]
    public void BadRequest_WithField()
    {
        var error = ResultError.BadRequest("INVALID_EMAIL", "Invalid email format.", "email");

        output.WriteLine($"Code: {error.Code}, Field: {error.Field}");

        Assert.Equal(ResultErrorKind.BadRequest, error.Kind);
        Assert.Equal("email", error.Field);
    }

    [Fact]
    public void Validation_ProducesCorrectKind()
    {
        var error = ResultError.Validation("Name is required.", "name");

        Assert.Equal(ResultErrorKind.Validation, error.Kind);
        Assert.Equal("VALIDATION_ERROR", error.Code);
        Assert.Equal("name", error.Field);
    }

    [Fact]
    public void Internal_ProducesCorrectKind()
    {
        var error = ResultError.Internal("Something went wrong.");

        Assert.Equal(ResultErrorKind.Internal, error.Kind);
        Assert.Equal("INTERNAL_ERROR", error.Code);
    }

    [Fact]
    public void DomainErrors_HaveConsistentCodes()
    {
        output.WriteLine("=== Domain Error Catalog ===");
        var errors = new[]
        {
            ResultErrors.ClusterNotFound("c1"),
            ResultErrors.RoleNotFound("r1"),
            ResultErrors.UserNotFound(),
            ResultErrors.DuplicateClusterName("prod"),
            ResultErrors.StaticAssignmentExists(),
            ResultErrors.InvalidDuration("bad"),
            ResultErrors.CredentialNotFound(),
        };

        foreach (var e in errors)
            output.WriteLine($"  [{e.Kind}] {e.Code}: {e.Message}");

        Assert.All(errors, e =>
        {
            Assert.False(string.IsNullOrEmpty(e.Code));
            Assert.False(string.IsNullOrEmpty(e.Message));
        });
    }
}
