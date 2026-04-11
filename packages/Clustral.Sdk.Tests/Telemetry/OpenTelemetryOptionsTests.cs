using System.ComponentModel.DataAnnotations;
using Clustral.Sdk.Telemetry;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Telemetry;

public sealed class OpenTelemetryOptionsTests(ITestOutputHelper output)
{
    [Fact]
    public void ValidConfig_BindsCorrectly()
    {
        var options = new OpenTelemetryOptions
        {
            ServiceName = "clustral-controlplane",
            OtlpEndpoint = "http://tempo:4317",
        };

        output.WriteLine($"ServiceName={options.ServiceName}, OtlpEndpoint={options.OtlpEndpoint}");

        var results = ValidateOptions(options);
        results.Should().BeEmpty("all required fields are populated");

        options.ServiceName.Should().Be("clustral-controlplane");
        options.OtlpEndpoint.Should().Be("http://tempo:4317");
    }

    [Fact]
    public void MissingServiceName_IsInvalid()
    {
        var options = new OpenTelemetryOptions
        {
            ServiceName = null!,
            OtlpEndpoint = "http://tempo:4317",
        };

        output.WriteLine("Testing with ServiceName=null");

        var results = ValidateOptions(options);
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OpenTelemetryOptions.ServiceName)),
            "ServiceName is marked [Required]");
    }

    [Fact]
    public void MissingOtlpEndpoint_UsesDefault()
    {
        var options = new OpenTelemetryOptions
        {
            ServiceName = "clustral-audit-service",
        };

        output.WriteLine($"Default OtlpEndpoint={options.OtlpEndpoint}");

        options.OtlpEndpoint.Should().Be("http://localhost:4317",
            "the default OTLP endpoint should be localhost:4317");
    }

    private static List<ValidationResult> ValidateOptions(OpenTelemetryOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);
        return results;
    }
}
