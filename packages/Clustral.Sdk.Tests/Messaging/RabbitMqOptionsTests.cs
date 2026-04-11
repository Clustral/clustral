using System.ComponentModel.DataAnnotations;
using Clustral.Sdk.Messaging;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Messaging;

public sealed class RabbitMqOptionsTests(ITestOutputHelper output)
{
    [Fact]
    public void ValidConfig_BindsCorrectly()
    {
        var options = new RabbitMqOptions
        {
            Host = "rabbitmq.prod.internal",
            Port = 5673,
            VHost = "/clustral",
            User = "svc-clustral",
            Pass = "s3cret",
        };

        output.WriteLine($"Host={options.Host}, Port={options.Port}, VHost={options.VHost}");

        var results = ValidateOptions(options);
        results.Should().BeEmpty("all required fields are populated");

        options.Host.Should().Be("rabbitmq.prod.internal");
        options.Port.Should().Be(5673);
        options.VHost.Should().Be("/clustral");
        options.User.Should().Be("svc-clustral");
        options.Pass.Should().Be("s3cret");
    }

    [Fact]
    public void DefaultValues_Applied()
    {
        var options = new RabbitMqOptions();

        output.WriteLine($"Default Host={options.Host}, Port={options.Port}, VHost={options.VHost}");

        options.Host.Should().Be("localhost");
        options.Port.Should().Be(5672);
        options.VHost.Should().Be("/");
        options.User.Should().Be("guest");
        options.Pass.Should().Be("guest");
    }

    [Fact]
    public void MissingHost_IsInvalid()
    {
        var options = new RabbitMqOptions
        {
            Host = null!,
            User = "svc",
            Pass = "pw",
        };

        output.WriteLine("Testing with Host=null");

        var results = ValidateOptions(options);
        results.Should().Contain(r => r.MemberNames.Contains(nameof(RabbitMqOptions.Host)),
            "Host is marked [Required]");
    }

    private static List<ValidationResult> ValidateOptions(RabbitMqOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);
        return results;
    }
}
