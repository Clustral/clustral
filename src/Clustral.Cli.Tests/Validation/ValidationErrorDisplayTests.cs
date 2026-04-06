using Clustral.Cli.Ui;
using FluentAssertions;
using FluentValidation.Results;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Validation;

public sealed class ValidationErrorDisplayTests(ITestOutputHelper output)
{
    [Fact]
    public void WriteValidationErrors_ShowsFieldAndMessage()
    {
        var console = new TestConsole();
        console.Profile.Width = 80;

        var errors = new List<ValidationFailure>
        {
            new("ClusterId", "Cluster ID must be a valid GUID."),
        };

        CliErrors.WriteValidationErrors(console, errors);

        output.WriteLine("=== Output ===");
        output.WriteLine(console.Output);

        console.Output.Should().Contain("Validation");
        console.Output.Should().Contain("ClusterId");
        console.Output.Should().Contain("Cluster ID must be a valid GUID.");
    }

    [Fact]
    public void WriteValidationErrors_MultipleErrors_ShowsAll()
    {
        var console = new TestConsole();
        console.Profile.Width = 80;

        var errors = new List<ValidationFailure>
        {
            new("ClusterId", "Cluster ID must be a valid GUID."),
            new("Ttl", "TTL must be a valid ISO 8601 duration."),
        };

        CliErrors.WriteValidationErrors(console, errors);

        output.WriteLine("=== Output ===");
        output.WriteLine(console.Output);

        console.Output.Should().Contain("ClusterId");
        console.Output.Should().Contain("Ttl");
        console.Output.Should().Contain("ISO 8601");
    }

    [Fact]
    public void WriteValidationErrors_UsesYellowBorder()
    {
        var console = new TestConsole();
        console.Profile.Width = 80;

        var errors = new List<ValidationFailure>
        {
            new("Role", "Role is required."),
        };

        CliErrors.WriteValidationErrors(console, errors);

        output.WriteLine("=== Output ===");
        output.WriteLine(console.Output);

        // The panel header should contain "Validation".
        console.Output.Should().Contain("Validation");
        console.Output.Should().Contain("Role");
        console.Output.Should().Contain("Role is required.");
    }

    [Fact]
    public void WriteValidationErrors_EscapesMarkup()
    {
        var console = new TestConsole();
        console.Profile.Width = 80;

        var errors = new List<ValidationFailure>
        {
            new("Field", "Value must not contain [brackets] or <tags>."),
        };

        CliErrors.WriteValidationErrors(console, errors);

        output.WriteLine("=== Output ===");
        output.WriteLine(console.Output);

        // Should render without Spectre.Console markup errors.
        console.Output.Should().Contain("Field");
    }
}
