using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure.Auth;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Shared;

public class CredentialOptionsValidatorTests(ITestOutputHelper output)
{
    private readonly CredentialOptionsValidator _validator = new();

    private static CredentialOptions ValidOptions() => new()
    {
        DefaultKubeconfigCredentialTtl = TimeSpan.FromHours(8),
        MaxKubeconfigCredentialTtl = TimeSpan.FromHours(8),
    };

    [Fact]
    public void ValidConfig_Passes()
    {
        var opts = ValidOptions();

        var result = _validator.Validate(opts);

        output.WriteLine($"IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ZeroDefaultTtl_Fails()
    {
        var opts = ValidOptions();
        opts.DefaultKubeconfigCredentialTtl = TimeSpan.Zero;

        var result = _validator.Validate(opts);

        output.WriteLine($"Errors: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DefaultKubeconfigCredentialTtl");
    }

    [Fact]
    public void MaxTtlLessThanDefaultTtl_Fails()
    {
        var opts = ValidOptions();
        opts.DefaultKubeconfigCredentialTtl = TimeSpan.FromHours(8);
        opts.MaxKubeconfigCredentialTtl = TimeSpan.FromHours(4);

        var result = _validator.Validate(opts);

        output.WriteLine($"Errors: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MaxKubeconfigCredentialTtl");
    }

    [Fact]
    public void CustomTtlValues_Pass()
    {
        var opts = new CredentialOptions
        {
            DefaultKubeconfigCredentialTtl = TimeSpan.FromHours(4),
            MaxKubeconfigCredentialTtl = TimeSpan.FromHours(12),
        };

        var result = _validator.Validate(opts);

        output.WriteLine($"IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }
}
