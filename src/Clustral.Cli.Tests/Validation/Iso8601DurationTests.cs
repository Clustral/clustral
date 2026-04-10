using Clustral.Cli.Validation;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Validation;

public sealed class Iso8601DurationTests(ITestOutputHelper output)
{
    // ─────────────────────────────────────────────────────────────────────────
    // Normalize — shorthand → full ISO 8601
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("8H",       "PT8H")]
    [InlineData("30M",      "PT30M")]
    [InlineData("1S",       "PT1S")]
    [InlineData("1D",       "P1D")]
    [InlineData("2W",       "P2W")]
    [InlineData("1D12H30M", "P1DT12H30M")]
    [InlineData("1D8H",     "P1DT8H")]
    [InlineData("2D30M",    "P2DT30M")]
    public void Normalize_Shorthand_ConvertsToIso8601(string input, string expected)
    {
        var result = Iso8601Duration.Normalize(input);

        output.WriteLine($"Normalize: '{input}' → '{result}'");
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("8h",  "PT8H")]
    [InlineData("30m", "PT30M")]
    [InlineData("1d",  "P1D")]
    [InlineData("1d8h30m", "P1DT8H30M")]
    public void Normalize_Lowercase_NormalizesToUpper(string input, string expected)
    {
        var result = Iso8601Duration.Normalize(input);

        output.WriteLine($"Normalize (lowercase): '{input}' → '{result}'");
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("PT8H")]
    [InlineData("P1D")]
    [InlineData("P1DT12H30M")]
    [InlineData("PT30S")]
    [InlineData("P2W")]
    public void Normalize_AlreadyIso8601_PassesThrough(string input)
    {
        var result = Iso8601Duration.Normalize(input);

        output.WriteLine($"Pass-through: '{input}' → '{result}'");
        result.Should().Be(input, "already-valid ISO 8601 should not be modified");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("hours")]
    [InlineData("---")]
    public void Normalize_InvalidInput_ReturnsUnchanged(string input)
    {
        var result = Iso8601Duration.Normalize(input);

        output.WriteLine($"Invalid: '{input}' → '{result}'");
        result.Should().Be(input, "unrecognizable input should pass through for the validator to reject");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Normalize_NullOrEmpty_ReturnsAsIs(string? input)
    {
        var result = Iso8601Duration.Normalize(input!);

        output.WriteLine($"Null/empty: '{input}' → '{result}'");
        result.Should().Be(input);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Normalize + IsValid — end-to-end shorthand acceptance
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("8H")]
    [InlineData("30M")]
    [InlineData("1D")]
    [InlineData("2W")]
    [InlineData("1D12H30M")]
    [InlineData("8h")]
    [InlineData("PT8H")]
    [InlineData("P1D")]
    public void IsValid_AfterNormalize_AcceptsShorthand(string input)
    {
        var normalized = Iso8601Duration.Normalize(input);
        var valid = Iso8601Duration.IsValid(normalized);

        output.WriteLine($"'{input}' → '{normalized}' → IsValid: {valid}");
        valid.Should().BeTrue($"'{input}' normalizes to '{normalized}' which is valid ISO 8601");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("hours")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_AfterNormalize_StillRejectsGarbage(string? input)
    {
        var normalized = Iso8601Duration.Normalize(input!);
        var valid = Iso8601Duration.IsValid(normalized);

        output.WriteLine($"'{input}' → '{normalized}' → IsValid: {valid}");
        valid.Should().BeFalse();
    }
}
