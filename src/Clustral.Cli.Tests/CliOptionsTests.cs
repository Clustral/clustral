using FluentAssertions;

namespace Clustral.Cli.Tests;

/// <summary>
/// Tests for <see cref="CliOptions"/> — the global output format flag
/// used by all list commands to switch between table and JSON rendering.
/// </summary>
public sealed class CliOptionsTests : IDisposable
{
    private readonly string _origFormat = CliOptions.OutputFormat;

    public void Dispose() => CliOptions.OutputFormat = _origFormat;

    [Fact]
    public void DefaultFormat_IsTable()
    {
        CliOptions.OutputFormat = "table";
        CliOptions.IsJson.Should().BeFalse();
    }

    [Fact]
    public void JsonFormat_IsJson()
    {
        CliOptions.OutputFormat = "json";
        CliOptions.IsJson.Should().BeTrue();
    }

    [Fact]
    public void JsonFormat_CaseInsensitive()
    {
        CliOptions.OutputFormat = "JSON";
        CliOptions.IsJson.Should().BeTrue();

        CliOptions.OutputFormat = "Json";
        CliOptions.IsJson.Should().BeTrue();
    }

    [Fact]
    public void TableFormat_IsNotJson()
    {
        CliOptions.OutputFormat = "table";
        CliOptions.IsJson.Should().BeFalse();

        CliOptions.OutputFormat = "TABLE";
        CliOptions.IsJson.Should().BeFalse();
    }
}
