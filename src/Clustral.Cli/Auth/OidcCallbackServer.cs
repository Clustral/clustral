using System.Net;
using System.Web;

namespace Clustral.Cli.Auth;

/// <summary>
/// Minimal HTTP server that listens on <c>http://127.0.0.1:{port}/callback</c>
/// for a single OAuth2 authorization-code redirect.
/// </summary>
/// <remarks>
/// Uses <see cref="HttpListener"/> which is AOT-compatible on all platforms.
/// Serves a small HTML success / error page so the browser gives the user
/// immediate visual feedback after the redirect.
/// </remarks>
internal sealed class OidcCallbackServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private bool _disposed;

    public OidcCallbackServer(int port)
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
    }

    /// <summary>
    /// Waits for the single OAuth2 callback request, returns the raw query
    /// string (e.g. <c>code=abc&amp;state=xyz</c>), then serves a response
    /// page to the browser.
    /// </summary>
    public async Task<string> WaitForCallbackAsync(CancellationToken ct)
    {
        using var reg = ct.Register(_listener.Stop);

        HttpListenerContext ctx;
        try
        {
            ctx = await _listener.GetContextAsync();
        }
        catch (HttpListenerException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        var query     = ctx.Request.Url?.Query ?? string.Empty;
        var hasError  = query.Contains("error=", StringComparison.Ordinal);
        var html      = hasError ? ErrorPage(query) : SuccessPage();

        ctx.Response.ContentType     = "text/html; charset=utf-8";
        ctx.Response.StatusCode      = 200;
        ctx.Response.ContentLength64 = System.Text.Encoding.UTF8.GetByteCount(html);
        await using (var sw = new StreamWriter(ctx.Response.OutputStream))
            await sw.WriteAsync(html);

        ctx.Response.Close();
        return query.TrimStart('?');
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _listener.Stop(); } catch { /* ignore */ }
        _listener.Close();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static string SuccessPage() => """
        <!doctype html><html><body style="font-family:system-ui;text-align:center;padding:3rem">
        <h2>&#x2713; Logged in to Clustral</h2>
        <p>You can close this tab and return to the terminal.</p>
        </body></html>
        """;

    private static string ErrorPage(string query) => $"""
        <!doctype html><html><body style="font-family:system-ui;text-align:center;padding:3rem">
        <h2>&#x2717; Authentication failed</h2>
        <p>{HttpUtility.HtmlEncode(query)}</p>
        <p>Check the terminal for details.</p>
        </body></html>
        """;
}
