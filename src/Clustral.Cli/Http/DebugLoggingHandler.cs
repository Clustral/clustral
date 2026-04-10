using System.Diagnostics;
using Spectre.Console;

namespace Clustral.Cli.Http;

/// <summary>
/// HTTP message handler that logs request/response details when
/// <see cref="CliDebug.Enabled"/> is <c>true</c>. Inserted into the
/// <see cref="HttpClient"/> pipeline by <see cref="CliHttp.CreateClient"/>.
/// </summary>
internal sealed class DebugLoggingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (!CliDebug.Enabled)
            return await base.SendAsync(request, ct);

        AnsiConsole.MarkupLine($"[dim]  → {request.Method} {request.RequestUri?.ToString().EscapeMarkup()}[/]");

        var sw = Stopwatch.StartNew();
        var response = await base.SendAsync(request, ct);
        sw.Stop();

        AnsiConsole.MarkupLine(
            $"[dim]  ← {(int)response.StatusCode} {response.StatusCode} ({sw.ElapsedMilliseconds}ms)[/]");

        return response;
    }
}
