using Clustral.Cli.Http;
using Spectre.Console;

namespace Clustral.Cli.Ui;

/// <summary>
/// Global exception handler for the CLI. Classifies unhandled exceptions via
/// pattern matching and renders the appropriate error output. Mirrors the
/// ControlPlane's <c>GlobalExceptionHandlerMiddleware.ClassifyException</c>.
///
/// Adding a new exception type = adding one case to the switch expression.
/// </summary>
internal static class CliExceptionHandler
{
    /// <summary>
    /// Classifies and renders an unhandled exception.
    /// Returns the process exit code.
    /// </summary>
    public static int Handle(Exception ex) => Handle(AnsiConsole.Console, ex);

    /// <summary>
    /// Testable overload that accepts an <see cref="IAnsiConsole"/>.
    /// </summary>
    public static int Handle(IAnsiConsole console, Exception ex)
    {
        var (exitCode, render) = Classify(ex);

        if (exitCode != 130) // don't render for user-initiated cancellation
            render(console);

        if (CliDebug.Enabled && exitCode != 130)
        {
            console.WriteLine();
            console.MarkupLine("[dim]Exception details:[/]");
            console.WriteLine(ex.ToString());
        }

        return exitCode;
    }

    /// <summary>
    /// Switch-expression classification. Each case returns an exit code and
    /// a render action that writes the error to the console. Extensible: add
    /// new exception types as new cases in the switch.
    /// </summary>
    internal static (int ExitCode, Action<IAnsiConsole> Render) Classify(Exception ex) => ex switch
    {
        CliHttpTimeoutException =>
            (1, c => CliErrors.WriteError(c, Messages.Errors.Timeout)),

        CliHttpErrorException http =>
            (1, c => CliErrors.WriteHttpError(c, http.StatusCode, http.ResponseBody)),

        HttpRequestException =>
            (1, c => CliErrors.WriteConnectionError(c, ex)),

        TaskCanceledException =>
            (1, c => CliErrors.WriteConnectionError(c, ex)),

        OperationCanceledException =>
            (130, _ => { }),

        _ =>
            (1, c => CliErrors.WriteUnhandledException(c, ex)),
    };
}
