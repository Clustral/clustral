using Clustral.ControlPlane.Infrastructure.Auth;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Infrastructure;

public class CredentialOptionsTests(ITestOutputHelper output)
{
    [Fact]
    public void SectionName_IsCredential()
    {
        output.WriteLine($"SectionName: {CredentialOptions.SectionName}");

        Assert.Equal("Credential", CredentialOptions.SectionName);
    }

    [Fact]
    public void DefaultCredentialTtl_Is8Hours()
    {
        var opts = new CredentialOptions();

        output.WriteLine($"DefaultKubeconfigCredentialTtl: {opts.DefaultKubeconfigCredentialTtl}");

        Assert.Equal(TimeSpan.FromHours(8), opts.DefaultKubeconfigCredentialTtl);
    }

    [Fact]
    public void MaxCredentialTtl_Is8Hours()
    {
        var opts = new CredentialOptions();

        output.WriteLine($"MaxKubeconfigCredentialTtl: {opts.MaxKubeconfigCredentialTtl}");

        Assert.Equal(TimeSpan.FromHours(8), opts.MaxKubeconfigCredentialTtl);
    }

    [Fact]
    public void TtlCapping_RequestedExceedsMax_CappedToMax()
    {
        var opts = new CredentialOptions
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
