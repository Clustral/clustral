using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure.Auth;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Shared;

public class OidcOptionsValidatorTests(ITestOutputHelper output)
{
    private readonly OidcOptionsValidator _validator = new();

    private static OidcOptions ValidOptions() => new()
    {
        Authority = "http://localhost:8080/realms/clustral",
        ClientId = "clustral-control-plane",
        DefaultKubeconfigCredentialTtl = TimeSpan.FromHours(8),
        MaxKubeconfigCredentialTtl = TimeSpan.FromHours(8),
    };

    [Fact]
    public void EmptyAuthority_Fails()
    {
        var opts = ValidOptions();
        opts.Authority = string.Empty;

        var result = _validator.Validate(opts);

        output.WriteLine($"Errors: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Authority");
    }

    [Fact]
    public void InvalidAuthorityUrl_Fails()
    {
        var opts = ValidOptions();
        opts.Authority = "not-a-url";

        var result = _validator.Validate(opts);

        output.WriteLine($"Errors: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Authority");
    }

    [Fact]
    public void ValidConfig_Passes()
    {
        var opts = ValidOptions();

        var result = _validator.Validate(opts);

        output.WriteLine($"IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmptyClientId_Fails()
    {
        var opts = ValidOptions();
        opts.ClientId = string.Empty;

        var result = _validator.Validate(opts);

        output.WriteLine($"Errors: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ClientId");
    }

    [Fact]
    public void InvalidMetadataAddress_Fails()
    {
        var opts = ValidOptions();
        opts.MetadataAddress = "not-a-url";

        var result = _validator.Validate(opts);

        output.WriteLine($"Errors: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MetadataAddress");
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
    public void AllValid_Passes()
    {
        var opts = new OidcOptions
        {
            Authority = "https://auth.example.com/realms/clustral",
            ClientId = "my-client",
            Audience = "my-audience",
            MetadataAddress = "https://auth.example.com/realms/clustral/.well-known/openid-configuration",
            DefaultKubeconfigCredentialTtl = TimeSpan.FromHours(4),
            MaxKubeconfigCredentialTtl = TimeSpan.FromHours(12),
        };

        var result = _validator.Validate(opts);

        output.WriteLine($"IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }
}
