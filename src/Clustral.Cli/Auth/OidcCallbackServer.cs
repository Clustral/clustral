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
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Clustral — Logged in</title>
          <style>
            *{margin:0;padding:0;box-sizing:border-box}
            body{font-family:system-ui,-apple-system,sans-serif;min-height:100vh;display:flex;
                 flex-direction:column;align-items:center;justify-content:center;text-align:center;
                 background:#fafafa;color:#18181b}
            .icon{font-size:3.5rem;margin-bottom:1.25rem;animation:pop .4s ease-out}
            .icon span{display:inline-block;width:72px;height:72px;line-height:72px;border-radius:50%;
                       background:#dcfce7;color:#16a34a;font-weight:bold}
            h1{font-size:1.25rem;font-weight:600;margin-bottom:.5rem;color:#18181b}
            p{color:#71717a;font-size:.9rem}
            @keyframes pop{0%{transform:scale(0);opacity:0}60%{transform:scale(1.15)}100%{transform:scale(1);opacity:1}}
            @media(prefers-color-scheme:dark){
              body{background:#0f1117;color:#e4e4e7}
              h1{color:#e4e4e7}p{color:#a1a1aa}
              .icon span{background:#22c55e22;color:#22c55e}
            }
          </style>
        </head>
        <body>
          <div class="icon"><span>&#x2713;</span></div>
          <h1>Logged in to Clustral</h1>
          <p>You can close this tab and return to the terminal.</p>
        </body>
        </html>
        """;

    private static string ErrorPage(string query) => $$$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Clustral — Authentication failed</title>
          <style>
            *{margin:0;padding:0;box-sizing:border-box}
            body{font-family:system-ui,-apple-system,sans-serif;min-height:100vh;display:flex;flex-direction:column;
                 align-items:center;justify-content:center;text-align:center;
                 background:#fafafa;color:#18181b}
            .icon{font-size:3.5rem;margin-bottom:1.25rem;animation:pop .4s ease-out}
            .icon span{display:inline-block;width:72px;height:72px;line-height:72px;border-radius:50%;
                       background:#fee2e2;color:#dc2626;font-weight:bold}
            h1{font-size:1.25rem;font-weight:600;margin-bottom:.5rem;color:#18181b}
            p{color:#71717a;font-size:.9rem}
            pre{margin-top:1.25rem;padding:.75rem 1rem;border-radius:8px;background:#f4f4f5;
                color:#52525b;font-size:.75rem;text-align:left;max-width:480px;width:100%;
                overflow-x:auto;white-space:pre-wrap;word-break:break-all}
            @keyframes pop{0%{transform:scale(0);opacity:0}60%{transform:scale(1.15)}100%{transform:scale(1);opacity:1}}
            @media(prefers-color-scheme:dark){
              body{background:#0f1117;color:#e4e4e7}
              h1{color:#e4e4e7}p{color:#a1a1aa}
              .icon span{background:#ef444422;color:#ef4444}
              pre{background:#1a1d27;color:#a1a1aa}
            }
          </style>
        </head>
        <body>
          <div class="icon"><span>&#x2717;</span></div>
          <h1>Authentication failed</h1>
          <p>Check the terminal for details.</p>
          <pre>{{{HttpUtility.HtmlEncode(query)}}}</pre>
        </body>
        </html>
        """;
}
