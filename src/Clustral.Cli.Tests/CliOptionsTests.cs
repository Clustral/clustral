using System.CommandLine;
using FluentAssertions;

namespace Clustral.Cli.Tests;

/// <summary>
/// Tests for <see cref="CliOptions"/> and global CLI options (--output, --no-color).
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

    // ── --no-color ──────────────────────────────────────────────────────────

    [Fact]
    public void NoColor_ParsedAsGlobalOption()
    {
        // Verify the root command accepts --no-color without error.
        var root = new RootCommand("test");
        var noColorOption = new Option<bool>("--no-color");
        root.AddGlobalOption(noColorOption);

        var result = root.Parse(["--no-color"]);

        result.GetValueForOption(noColorOption).Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void NoColor_DefaultIsFalse()
    {
        var root = new RootCommand("test");
        var noColorOption = new Option<bool>("--no-color");
        root.AddGlobalOption(noColorOption);

        var result = root.Parse([]);

        result.GetValueForOption(noColorOption).Should().BeFalse();
    }
}
