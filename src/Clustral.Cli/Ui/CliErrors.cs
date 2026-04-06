using System.Text.Json;
using Spectre.Console;

namespace Clustral.Cli.Ui;

/// <summary>
/// Rich card-style error display for CLI commands using Spectre.Console panels.
/// Parses RFC 7807 Problem Details from ControlPlane responses and renders
/// structured error cards that are easy to read and copy into GitHub issues.
/// </summary>
internal static class CliErrors
{
    /// <summary>
    /// Displays an error card from an HTTP response (status code + body).
    /// Parses Problem Details JSON if available.
    /// </summary>
    internal static void WriteHttpError(int statusCode, string responseBody) =>
        WriteHttpError(AnsiConsole.Console, statusCode, responseBody);

    internal static void WriteHttpError(IAnsiConsole console, int statusCode, string responseBody)
    {
        var (message, code, field, traceId) = ParseProblemDetails(statusCode, responseBody);
        var hint = GetStatusHint(statusCode);
        var statusLabel = GetStatusLabel(statusCode);

        var table = new Table().Border(TableBorder.None).HideHeaders()
            .AddColumn("Key").AddColumn("Value");

        table.AddRow("[grey]Status[/]", $"[bold]{statusCode}[/] {statusLabel.EscapeMarkup()}");
        table.AddRow("[grey]Message[/]", message.EscapeMarkup());

        if (code is not null)
            table.AddRow("[grey]Code[/]", $"[dim]{code.EscapeMarkup()}[/]");
        if (field is not null)
            table.AddRow("[grey]Field[/]", $"[yellow]{field.EscapeMarkup()}[/]");
        if (traceId is not null)
            table.AddRow("[grey]Trace ID[/]", $"[dim]{traceId.EscapeMarkup()}[/]");
        if (hint is not null)
            table.AddRow("[grey]Hint[/]", $"[cyan]{hint.EscapeMarkup()}[/]");

        var panel = new Panel(table)
            .Header("[red] Error [/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Red))
            .Padding(1, 0);

        console.WriteLine();
        console.Write(panel);
        console.WriteLine();
    }

    /// <summary>
    /// Displays a connection/network error card with helpful context.
    /// </summary>
    internal static void WriteConnectionError(Exception ex) =>
        WriteConnectionError(AnsiConsole.Console, ex);

    internal static void WriteConnectionError(IAnsiConsole console, Exception ex)
    {
        var (title, message, hint) = ClassifyConnectionError(ex);

        var table = new Table().Border(TableBorder.None).HideHeaders()
            .AddColumn("Key").AddColumn("Value");

        table.AddRow("[grey]Type[/]", title.EscapeMarkup());
        table.AddRow("[grey]Detail[/]", message.EscapeMarkup());
        if (hint is not null)
            table.AddRow("[grey]Hint[/]", $"[cyan]{hint.EscapeMarkup()}[/]");
        table.AddRow("[grey]Exception[/]", $"[dim]{ex.GetType().Name.EscapeMarkup()}[/]");

        var panel = new Panel(table)
            .Header("[red] Connection Error [/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Red))
            .Padding(1, 0);

        console.WriteLine();
        console.Write(panel);
        console.WriteLine();
    }

    /// <summary>
    /// Displays a simple error card.
    /// </summary>
    internal static void WriteError(string message) =>
        WriteError(AnsiConsole.Console, message);

    internal static void WriteError(IAnsiConsole console, string message)
    {
        var panel = new Panel($"  {message.EscapeMarkup()}")
            .Header("[red] Error [/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Red))
            .Padding(1, 0);

        console.WriteLine();
        console.Write(panel);
        console.WriteLine();
    }

    /// <summary>
    /// Displays a "not configured" error card with a fix command.
    /// </summary>
    internal static void WriteNotConfigured(string what, string fix) =>
        WriteNotConfigured(AnsiConsole.Console, what, fix);

    internal static void WriteNotConfigured(IAnsiConsole console, string what, string fix)
    {
        var table = new Table().Border(TableBorder.None).HideHeaders()
            .AddColumn("Key").AddColumn("Value");

        table.AddRow("[grey]Issue[/]", what.EscapeMarkup());
        table.AddRow("[grey]Fix[/]", $"[cyan]{fix.EscapeMarkup()}[/]");

        var panel = new Panel(table)
            .Header("[yellow] Config [/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Yellow))
            .Padding(1, 0);

        console.WriteLine();
        console.Write(panel);
        console.WriteLine();
    }

    /// <summary>
    /// Displays a validation error card listing each invalid field and its message.
    /// </summary>
    internal static void WriteValidationErrors(IReadOnlyList<FluentValidation.Results.ValidationFailure> errors) =>
        WriteValidationErrors(AnsiConsole.Console, errors);

    internal static void WriteValidationErrors(IAnsiConsole console, IReadOnlyList<FluentValidation.Results.ValidationFailure> errors)
    {
        var table = new Table().Border(TableBorder.None).HideHeaders()
            .AddColumn("Field").AddColumn("Message");

        foreach (var e in errors)
            table.AddRow(
                $"[yellow]{e.PropertyName.EscapeMarkup()}[/]",
                e.ErrorMessage.EscapeMarkup());

        var panel = new Panel(table)
            .Header("[yellow] Validation [/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Yellow))
            .Padding(1, 0);

        console.WriteLine();
        console.Write(panel);
        console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static (string Message, string? Code, string? Field, string? TraceId)
        ParseProblemDetails(int statusCode, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (GetDefaultMessage(statusCode), null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var message = root.TryGetProperty("detail", out var d) ? d.GetString()
                        : root.TryGetProperty("error", out var e) ? e.GetString()
                        : root.TryGetProperty("title", out var t) ? t.GetString()
                        : null;

            var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            var field = root.TryGetProperty("field", out var f) ? f.GetString() : null;
            var traceId = root.TryGetProperty("traceId", out var tr) ? tr.GetString() : null;

            return (message ?? GetDefaultMessage(statusCode), code, field, traceId);
        }
        catch
        {
            return (body.Length < 200 ? body.Trim() : GetDefaultMessage(statusCode), null, null, null);
        }
    }

    private static (string Title, string Message, string? Hint) ClassifyConnectionError(Exception ex) => ex switch
    {
        HttpRequestException { InnerException: System.Net.Sockets.SocketException } =>
            ("Connection refused", "Could not connect to the ControlPlane. Is it running?", "Check that the ControlPlane is started and accessible."),
        HttpRequestException hre when hre.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) =>
            ("SSL/TLS failure", "The SSL connection could not be established.", "Try: clustral <command> --insecure"),
        HttpRequestException hre when hre.Message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) =>
            ("DNS resolution failed", "The ControlPlane hostname could not be resolved.", "Check the URL in ~/.clustral/config.json"),
        TaskCanceledException =>
            ("Request timeout", "The request to the ControlPlane timed out.", "Check network connectivity."),
        OperationCanceledException =>
            ("Cancelled", "The operation was cancelled.", null),
        _ =>
            ("Unexpected error", ex.Message, null),
    };

    private static string GetDefaultMessage(int statusCode) => statusCode switch
    {
        401 => "Session expired or invalid credentials.",
        403 => "Access denied. You don't have permission for this action.",
        404 => "Resource not found.",
        409 => "Conflict — resource already exists or is in an incompatible state.",
        422 => "Invalid request data.",
        500 => "Internal server error. Check ControlPlane logs.",
        502 => "ControlPlane is unreachable or the cluster agent is disconnected.",
        503 => "Service temporarily unavailable.",
        _ => $"Unexpected error (HTTP {statusCode}).",
    };

    private static string GetStatusLabel(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "",
    };

    private static string? GetStatusHint(int statusCode) => statusCode switch
    {
        401 => "Run: clustral login",
        403 => "Request access: clustral access request --role <role> --cluster <cluster>",
        502 => "Check that the cluster agent is connected.",
        _ => null,
    };
}
