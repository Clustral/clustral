using System.Text;
using System.Text.RegularExpressions;

namespace Clustral.Cli.Validation;

/// <summary>
/// AOT-safe ISO 8601 duration validation and shorthand normalization.
/// Accepts both full ISO 8601 (<c>PT8H</c>) and shorthand (<c>8H</c>).
/// Shorthand is normalized to full ISO 8601 before validation / HTTP.
/// </summary>
internal static partial class Iso8601Duration
{
    // Matches full ISO 8601 patterns like P1D, PT8H, P1DT12H30M, PT30S.
    [GeneratedRegex(@"^P(?:\d+Y)?(?:\d+M)?(?:\d+W)?(?:\d+D)?(?:T(?:\d+H)?(?:\d+M)?(?:\d+S)?)?$")]
    private static partial Regex Pattern();

    // Matches individual duration components like "8H", "30M", "1D".
    [GeneratedRegex(@"\d+[A-Za-z]")]
    private static partial Regex ComponentPattern();

    internal static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value != "P" && value != "PT" && Pattern().IsMatch(value);

    /// <summary>
    /// Normalizes a shorthand duration (<c>8H</c>, <c>30M</c>, <c>1D12H30M</c>)
    /// to full ISO 8601 (<c>PT8H</c>, <c>PT30M</c>, <c>P1DT12H30M</c>).
    /// If the input already starts with <c>P</c>, it is returned unchanged.
    /// In shorthand, <c>M</c> is always treated as minutes (not months).
    /// </summary>
    internal static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.StartsWith('P'))
            return input;

        var datePart = new StringBuilder();
        var timePart = new StringBuilder();

        foreach (var m in ComponentPattern().EnumerateMatches(input.AsSpan()))
        {
            var segment = input.Substring(m.Index, m.Length).ToUpperInvariant();
            var unit = segment[^1];
            switch (unit)
            {
                case 'Y' or 'W' or 'D':
                    datePart.Append(segment);
                    break;
                case 'H' or 'M' or 'S':
                    timePart.Append(segment);
                    break;
            }
        }

        if (datePart.Length == 0 && timePart.Length == 0)
            return input; // can't normalize; let the validator reject it

        var result = new StringBuilder("P");
        result.Append(datePart);
        if (timePart.Length > 0)
        {
            result.Append('T');
            result.Append(timePart);
        }
        return result.ToString();
    }
}
