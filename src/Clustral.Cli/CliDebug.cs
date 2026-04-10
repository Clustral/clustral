namespace Clustral.Cli;

/// <summary>
/// Global debug flag for the CLI. When enabled via <c>--debug</c>, the CLI
/// prints HTTP request/response traces, full exception details (including
/// stack traces and inner exceptions), and operation timing.
/// </summary>
internal static class CliDebug
{
    public static bool Enabled { get; set; }
}
