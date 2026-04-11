using Clustral.ControlPlane.Features.Proxy.Commands;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Api;

/// <summary>
/// Thin HTTP adapter that bridges <c>HttpContext</c> to the
/// <see cref="ProxyKubectlRequestCommand"/> CQS handler. All business logic
/// (auth, impersonation, tunnel proxying, audit) lives in the handler.
///
/// Path format: <c>/api/proxy/{clusterId}/{k8s-api-path}</c>
/// </summary>
public sealed class KubectlProxyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, IMediator mediator)
    {
        var path = httpContext.Request.Path.Value ?? "";

        // ── Match proxy path ───────────────────────────────────────────────
        string afterProxy;
        if (path.StartsWith("/api/proxy/", StringComparison.OrdinalIgnoreCase))
            afterProxy = path["/api/proxy/".Length..];
        else if (path.StartsWith("/proxy/", StringComparison.OrdinalIgnoreCase))
            afterProxy = path["/proxy/".Length..];
        else
        {
            await next(httpContext);
            return;
        }

        var ct = httpContext.RequestAborted;

        // ── Parse cluster ID + k8s path ────────────────────────────────────
        var slashIdx = afterProxy.IndexOf('/');
        var clusterIdStr = slashIdx < 0 ? afterProxy : afterProxy[..slashIdx];
        var k8sPath = slashIdx < 0 ? "/" : afterProxy[slashIdx..];

        if (!Guid.TryParse(clusterIdStr, out var clusterId))
        {
            httpContext.Response.StatusCode = 400;
            await httpContext.Response.WriteAsync("Invalid cluster ID.", cancellationToken: ct);
            return;
        }

        if (httpContext.Request.QueryString.HasValue)
            k8sPath += httpContext.Request.QueryString.Value;

        // ── Extract bearer token ───────────────────────────────────────────
        var bearerToken = ExtractBearerToken(httpContext.Request);
        if (bearerToken is null)
        {
            httpContext.Response.StatusCode = 401;
            await httpContext.Response.WriteAsync(
                "Authorization: Bearer token required.", cancellationToken: ct);
            return;
        }

        // ── Collect forwarded headers ──────────────────────────────────────
        var headers = new List<ProxyHeader>();
        foreach (var (name, values) in httpContext.Request.Headers)
        {
            if (IsHopByHop(name) ||
                name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                continue;
            headers.Add(new ProxyHeader(name, string.Join(", ", values!)));
        }

        // ── Read request body ──────────────────────────────────────────────
        byte[] body = [];
        if (httpContext.Request.ContentLength > 0 ||
            httpContext.Request.Method is "POST" or "PUT" or "PATCH")
        {
            using var ms = new MemoryStream();
            await httpContext.Request.Body.CopyToAsync(ms, ct);
            body = ms.ToArray();
        }

        // ── Send CQS command ──────────────────────────────────────────────
        var result = await mediator.Send(new ProxyKubectlRequestCommand(
            clusterId, bearerToken, httpContext.Request.Method,
            k8sPath, headers, body), ct);

        // ── Write result to HTTP response ──────────────────────────────────
        if (result.IsFailure)
        {
            httpContext.Response.StatusCode = MapErrorToStatusCode(result.Error!);
            await httpContext.Response.WriteAsync(result.Error!.Message, cancellationToken: ct);
            return;
        }

        var response = result.Value;
        httpContext.Response.StatusCode = response.StatusCode;
        foreach (var h in response.Headers)
            httpContext.Response.Headers.Append(h.Name, h.Value);

        if (response.Body.Length > 0)
            await httpContext.Response.Body.WriteAsync(response.Body, ct);
    }

    /// <summary>
    /// Maps proxy-specific error codes to HTTP status codes. The handler
    /// uses <c>ResultErrorKind.Internal</c> for tunnel errors, but the
    /// middleware distinguishes 401/403/502/504 based on the error code.
    /// </summary>
    private static int MapErrorToStatusCode(ResultError error) => error.Code switch
    {
        "GATEWAY_TIMEOUT"     => 504,
        "AGENT_NOT_CONNECTED" => 502,
        "AGENT_ERROR"         => 502,
        "TUNNEL_ERROR"        => 502,
        _ => error.Kind switch
        {
            ResultErrorKind.Unauthorized => 401,
            ResultErrorKind.Forbidden    => 403,
            _                            => 500,
        },
    };

    private static string? ExtractBearerToken(HttpRequest request)
    {
        var auth = request.Headers.Authorization.FirstOrDefault();
        if (auth is null || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return auth["Bearer ".Length..].Trim();
    }

    private static readonly HashSet<string> HopByHopHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Connection", "Keep-Alive", "Proxy-Authenticate",
            "Proxy-Authorization", "TE", "Trailers",
            "Transfer-Encoding", "Upgrade",
        };

    private static bool IsHopByHop(string name) => HopByHopHeaders.Contains(name);
}
