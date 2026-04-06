using Clustral.ControlPlane.Infrastructure.Auth;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Infrastructure;

public class OidcOptionsTests(ITestOutputHelper output)
{
    [Fact]
    public void SectionName_IsOidc()
    {
        output.WriteLine($"SectionName: {OidcOptions.SectionName}");

        Assert.Equal("Oidc", OidcOptions.SectionName);
    }

    [Fact]
    public void DefaultCredentialTtl_Is8Hours()
    {
        var opts = new OidcOptions();

        output.WriteLine($"DefaultKubeconfigCredentialTtl: {opts.DefaultKubeconfigCredentialTtl}");

        Assert.Equal(TimeSpan.FromHours(8), opts.DefaultKubeconfigCredentialTtl);
    }

    [Fact]
    public void MaxCredentialTtl_Is8Hours()
    {
        var opts = new OidcOptions();

        output.WriteLine($"MaxKubeconfigCredentialTtl: {opts.MaxKubeconfigCredentialTtl}");

        Assert.Equal(TimeSpan.FromHours(8), opts.MaxKubeconfigCredentialTtl);
    }

    [Fact]
    public void RequireHttpsMetadata_DefaultsToTrue()
    {
        var opts = new OidcOptions();

        output.WriteLine($"RequireHttpsMetadata: {opts.RequireHttpsMetadata} (production default)");

        Assert.True(opts.RequireHttpsMetadata);
    }

    [Fact]
    public void Audience_EmptyByDefault_FallsBackToClientId()
    {
        var opts = new OidcOptions { ClientId = "clustral-api" };

        output.WriteLine($"ClientId: {opts.ClientId}");
        output.WriteLine($"Audience: \"{opts.Audience}\" (empty => use ClientId)");

        var effectiveAudience = string.IsNullOrEmpty(opts.Audience) ? opts.ClientId : opts.Audience;
        Assert.Equal("clustral-api", effectiveAudience);
    }

    [Fact]
    public void TtlCapping_RequestedExceedsMax_CappedToMax()
    {
        var opts = new OidcOptions
        {
            DefaultKubeconfigCredentialTtl = TimeSpan.FromHours(8),
            MaxKubeconfigCredentialTtl = TimeSpan.FromHours(12),
        };

        var testCases = new[]
        {
            (requested: TimeSpan.FromHours(4), expected: TimeSpan.FromHours(4), label: "under max"),
            (requested: TimeSpan.FromHours(12), expected: TimeSpan.FromHours(12), label: "at max"),
            (requested: TimeSpan.FromHours(24), expected: TimeSpan.FromHours(12), label: "over max"),
        };

        foreach (var (requested, expected, label) in testCases)
        {
            var capped = requested < opts.MaxKubeconfigCredentialTtl
                ? requested
                : opts.MaxKubeconfigCredentialTtl;

            output.WriteLine($"  {label}: requested={requested.TotalHours}h => {capped.TotalHours}h");

            Assert.Equal(expected, capped);
        }
    }
}
