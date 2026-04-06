using System.Text.RegularExpressions;

namespace Clustral.Cli.Validation;

/// <summary>
/// AOT-safe ISO 8601 duration validation using source-generated regex.
/// </summary>
internal static partial class Iso8601Duration
{
    // Matches patterns like P1D, PT8H, P1DT12H30M, PT30S, etc.
    [GeneratedRegex(@"^P(?:\d+Y)?(?:\d+M)?(?:\d+W)?(?:\d+D)?(?:T(?:\d+H)?(?:\d+M)?(?:\d+S)?)?$")]
    private static partial Regex Pattern();

    internal static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value != "P" && value != "PT" && Pattern().IsMatch(value);
}
