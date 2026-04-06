using Clustral.Cli.Validation;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Validation;

public sealed class AccessValidatorTests(ITestOutputHelper output)
{
    // ── AccessRequestValidator ──────────────────────────────────────────────

    [Fact]
    public void AccessRequest_Valid_Passes()
    {
        var input = new AccessRequestInput("admin", "prod-cluster", null);
        var result = new AccessRequestValidator().Validate(input);

        output.WriteLine($"Role: {input.Role}, Cluster: {input.Cluster} => IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void AccessRequest_WithValidDuration_Passes()
    {
        var input = new AccessRequestInput("admin", "prod-cluster", "PT4H");
        var result = new AccessRequestValidator().Validate(input);

        output.WriteLine($"Duration: {input.Duration} => IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void AccessRequest_EmptyRole_Fails()
    {
        var input = new AccessRequestInput("", "prod-cluster", null);
        var result = new AccessRequestValidator().Validate(input);

        output.WriteLine($"Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public void AccessRequest_EmptyCluster_Fails()
    {
        var input = new AccessRequestInput("admin", "", null);
        var result = new AccessRequestValidator().Validate(input);

        output.WriteLine($"Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Cluster");
    }

    [Fact]
    public void AccessRequest_InvalidDuration_Fails()
    {
        var input = new AccessRequestInput("admin", "prod-cluster", "invalid");
        var result = new AccessRequestValidator().Validate(input);

        output.WriteLine($"Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Duration");
    }

    [Fact]
    public void AccessRequest_AllEmpty_ReturnsMultipleErrors()
    {
        var input = new AccessRequestInput("", "", "bad");
        var result = new AccessRequestValidator().Validate(input);

        output.WriteLine($"Errors ({result.Errors.Count}):");
        foreach (var e in result.Errors)
            output.WriteLine($"  {e.PropertyName}: {e.ErrorMessage}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterOrEqualTo(3);
    }

    // ── AccessActionValidator (approve/revoke) ──────────────────────────────

    [Fact]
    public void AccessAction_ValidGuid_Passes()
    {
        var input = new AccessActionInput(Guid.NewGuid().ToString());
        var result = new AccessActionValidator().Validate(input);

        output.WriteLine($"RequestId: {input.RequestId} => IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("12345")]
    public void AccessAction_InvalidRequestId_Fails(string requestId)
    {
        var input = new AccessActionInput(requestId);
        var result = new AccessActionValidator().Validate(input);

        output.WriteLine($"RequestId: '{requestId}' => Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RequestId");
    }

    // ── AccessDenyValidator ─────────────────────────────────────────────────

    [Fact]
    public void AccessDeny_ValidInputs_Passes()
    {
        var input = new AccessDenyInput(Guid.NewGuid().ToString(), "Policy violation");
        var result = new AccessDenyValidator().Validate(input);

        output.WriteLine($"RequestId: {input.RequestId}, Reason: {input.Reason} => IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void AccessDeny_EmptyReason_Fails()
    {
        var input = new AccessDenyInput(Guid.NewGuid().ToString(), "");
        var result = new AccessDenyValidator().Validate(input);

        output.WriteLine($"Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Reason");
    }

    [Fact]
    public void AccessDeny_InvalidGuidAndEmptyReason_ReturnsMultipleErrors()
    {
        var input = new AccessDenyInput("bad-id", "");
        var result = new AccessDenyValidator().Validate(input);

        output.WriteLine($"Errors ({result.Errors.Count}):");
        foreach (var e in result.Errors)
            output.WriteLine($"  {e.PropertyName}: {e.ErrorMessage}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterOrEqualTo(2);
    }
}
