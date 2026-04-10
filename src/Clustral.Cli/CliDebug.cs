using Spectre.Console;

namespace Clustral.Cli;

/// <summary>
/// Global debug flag for the CLI. When enabled via <c>--debug</c>, the CLI
/// prints HTTP request/response traces, full exception details (including
/// stack traces and inner exceptions), operation timing, and step-by-step
/// progress via <see cref="Log"/>.
/// </summary>
internal static class CliDebug
{
    public static bool Enabled { get; set; }

    /// <summary>
    /// Writes a dim step-by-step log line when <see cref="Enabled"/> is true.
    /// Uses the <c>▸</c> indicator to distinguish from HTTP traces (<c>→</c>/<c>←</c>),
    /// timing (<c>⏱</c>), and swallowed exceptions (<c>DEBUG:</c>).
    /// </summary>
    public static void Log(string message)
    {
        if (!Enabled) return;
        AnsiConsole.MarkupLine($"[dim]  ▸ {message.EscapeMarkup()}[/]");
    }
}
