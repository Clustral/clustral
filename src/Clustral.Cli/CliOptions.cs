namespace Clustral.Cli;

/// <summary>
/// Global CLI options parsed once in <c>Program.cs</c> before command invocation.
/// Accessible from any command handler without parameter passing.
/// </summary>
internal static class CliOptions
{
    /// <summary>
    /// Output format: <c>"table"</c> (default) or <c>"json"</c>.
    /// Set from the <c>--output</c> global option.
    /// </summary>
    public static string OutputFormat { get; set; } = "table";

    public static bool IsJson => OutputFormat.Equals("json", StringComparison.OrdinalIgnoreCase);
}
