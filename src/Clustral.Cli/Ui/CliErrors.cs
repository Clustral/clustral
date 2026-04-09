using System.Text.Json;
using Spectre.Console;

namespace Clustral.Cli.Ui;

/// <summary>
/// Flat error and warning display for CLI commands. Each method renders a
/// coloured circle indicator + plain title on the first line, then the detail
/// rows in dimmed text below — no panels, no borders. Errors use a red
/// indicator; warnings use a yellow one. The detail rows match the dominant
/// <c>[dim]</c> convention used elsewhere in the CLI.
/// </summary>
internal static class CliErrors
{
    /// <summary>
    /// Displays an HTTP error from a response (status code + body).
    /// Parses Problem Details JSON if available.
    /// </summary>
    internal static void WriteHttpError(int statusCode, string responseBody) =>
        WriteHttpError(AnsiConsole.Console, statusCode, responseBody);

    internal static void WriteHttpError(IAnsiConsole console, int statusCode, string responseBody)
    {
        var (message, code, field, traceId) = ParseProblemDetails(statusCode, responseBody);
        var hint = GetStatusHint(statusCode);
        var statusLabel = GetStatusLabel(statusCode);

        WriteHeader(console, "red", "HTTP Error");

        var table = NewDetailTable();
        AddDimRow(table, "Status",  $"{statusCode} {statusLabel}".TrimEnd());
        AddDimRow(table, "Message", message);
        if (code is not null)    AddDimRow(table, "Code",     code);
        if (field is not null)   AddDimRow(table, "Field",    field);
        if (traceId is not null) AddDimRow(table, "Trace ID", traceId);
        if (hint is not null)    AddDimRow(table, "Hint",     hint);

        console.Write(table);
        console.WriteLine();
    }

    /// <summary>
    /// Displays a connection / network error with helpful context.
    /// </summary>
    internal static void WriteConnectionError(Exception ex) =>
        WriteConnectionError(AnsiConsole.Console, ex);

    internal static void WriteConnectionError(IAnsiConsole console, Exception ex)
    {
        var (title, message, hint) = ClassifyConnectionError(ex);

        WriteHeader(console, "red", "Connection Error");

        var table = NewDetailTable();
        AddDimRow(table, "Type",      title);
        AddDimRow(table, "Detail",    message);
        if (hint is not null)
            AddDimRow(table, "Hint",  hint);
        AddDimRow(table, "Exception", ex.GetType().Name);

        console.Write(table);
        console.WriteLine();
    }

    /// <summary>
    /// Displays a simple one-line error message.
    /// </summary>
    internal static void WriteError(string message) =>
        WriteError(AnsiConsole.Console, message);

    internal static void WriteError(IAnsiConsole console, string message)
    {
        WriteHeader(console, "red", "Error");

        var table = NewDetailTable();
        // 2-space prefix matches AddDimRow so the message indents under the title.
        table.AddRow("", $"  [dim]{message.EscapeMarkup()}[/]");

        console.Write(table);
        console.WriteLine();
    }

    /// <summary>
    /// Displays a "not configured" warning with a hint about how to fix it.
    /// </summary>
    internal static void WriteNotConfigured(string what, string hint) =>
        WriteNotConfigured(AnsiConsole.Console, what, hint);

    internal static void WriteNotConfigured(IAnsiConsole console, string what, string hint)
    {
        WriteHeader(console, "yellow", "Not configured");

        var table = NewDetailTable();
        AddDimRow(table, "Issue", what);
        AddDimRow(table, "Hint",  hint);

        console.Write(table);
        console.WriteLine();
    }

    /// <summary>
    /// Displays validation failures, one row per invalid field.
    /// </summary>
    internal static void WriteValidationErrors(IReadOnlyList<FluentValidation.Results.ValidationFailure> errors) =>
        WriteValidationErrors(AnsiConsole.Console, errors);

    internal static void WriteValidationErrors(IAnsiConsole console, IReadOnlyList<FluentValidation.Results.ValidationFailure> errors)
    {
        WriteHeader(console, "yellow", "Invalid input");

        var table = NewDetailTable();
        foreach (var e in errors)
            AddDimRow(table, e.PropertyName, e.ErrorMessage);

        console.Write(table);
        console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout helpers — shared by all five public methods.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a leading blank line, then the indicator + title line.
    /// The title is plain text (no markup), so the terminal renders it in
    /// the default foreground colour.
    /// </summary>
    private static void WriteHeader(IAnsiConsole console, string color, string title)
    {
        console.WriteLine();
        console.MarkupLine($"[{color}]●[/] {title.EscapeMarkup()}");
    }

    /// <summary>
    /// Returns an empty borderless 2-column table used for the dimmed detail
    /// rows below the header. The label column carries 2 spaces of leading
    /// whitespace inside its values so the rows visually indent under the
    /// indicator + title above them.
    /// </summary>
    private static Table NewDetailTable() =>
        new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Label"))
            .AddColumn(new TableColumn("Value"));

    /// <summary>Adds a `<c>  [dim]label[/]  [dim]value[/]</c>` row.</summary>
    private static void AddDimRow(Table table, string label, string value) =>
        table.AddRow(
            $"  [dim]{label.EscapeMarkup()}[/]",
            $"[dim]{value.EscapeMarkup()}[/]");

    // ─────────────────────────────────────────────────────────────────────────
    // Body classification — unchanged from the previous panel-based version.
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
