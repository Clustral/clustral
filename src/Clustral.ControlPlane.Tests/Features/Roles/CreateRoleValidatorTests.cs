using Clustral.ControlPlane.Features.Roles;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Roles;

public sealed class CreateRoleValidatorTests(ITestOutputHelper output)
{
    private readonly CreateRoleValidator _validator = new();

    [Fact]
    public void Validate_ValidName_Passes()
    {
        var cmd = new CreateRoleCommand("admin", "Full access", ["system:masters"]);
        var result = _validator.Validate(cmd);

        output.WriteLine($"Valid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        var cmd = new CreateRoleCommand("", "No name", null);
        var result = _validator.Validate(cmd);

        output.WriteLine($"Errors: {result.Errors.Count}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_NameTooLong_Fails()
    {
        var cmd = new CreateRoleCommand(new string('x', 101), "", null);
        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
    }
}
