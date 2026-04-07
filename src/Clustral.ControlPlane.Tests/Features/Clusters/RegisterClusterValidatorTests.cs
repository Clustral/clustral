using Clustral.ControlPlane.Features.Clusters;
using Clustral.ControlPlane.Features.Clusters.Commands;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Clusters;

public sealed class RegisterClusterValidatorTests(ITestOutputHelper output)
{
    private readonly RegisterClusterValidator _validator = new();

    [Fact]
    public void Validate_ValidName_Passes()
    {
        var command = new RegisterClusterCommand("production", "Main cluster", "", null);
        var result = _validator.Validate(command);

        output.WriteLine($"Valid: {result.IsValid}");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        var command = new RegisterClusterCommand("", "No name", "", null);
        var result = _validator.Validate(command);

        output.WriteLine($"Valid: {result.IsValid}, Errors: {result.Errors.Count}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_NullName_Fails()
    {
        var command = new RegisterClusterCommand(null!, "Null name", "", null);
        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NameTooLong_Fails()
    {
        var longName = new string('x', 101);
        var command = new RegisterClusterCommand(longName, "", "", null);
        var result = _validator.Validate(command);

        output.WriteLine($"Name length: {longName.Length}, Valid: {result.IsValid}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_Name100Chars_Passes()
    {
        var name = new string('x', 100);
        var command = new RegisterClusterCommand(name, "", "", null);
        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
