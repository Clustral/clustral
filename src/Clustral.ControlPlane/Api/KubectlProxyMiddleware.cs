using Clustral.ControlPlane.Features.Proxy.Commands;
using Clustral.Sdk.Http;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Api;

/// <summary>
/// Thin HTTP adapter that bridges <c>HttpContext</c> to the
/// <see cref="ProxyKubectlRequestCommand"/> CQS handler. All business logic
/// (auth, impersonation, tunnel proxying, audit) lives in the handler.
///
/// Path format: <c>/api/proxy/{clusterId}/{k8s-api-path}</c>
///
/// Error responses are written as Kubernetes <c>v1.Status</c> JSON via
/// <see cref="K8sStatusWriter"/> so <c>kubectl</c> natively renders them.
/// See the "Error Response Shapes" section of the root README for the
/// rationale behind the path-aware split.
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
            await K8sStatusWriter.WriteAsync(httpContext, ResultErrors.InvalidClusterId(clusterIdStr));
            return;
        }

        if (httpContext.Request.QueryString.HasValue)
            k8sPath += httpContext.Request.QueryString.Value;

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

        // ── Extract internal token (if request came through gateway) ──────
        var internalToken = httpContext.Request.Headers["X-Internal-Token"].FirstOrDefault();

        // ── Send CQS command ──────────────────────────────────────────────
        var result = await mediator.Send(new ProxyKubectlRequestCommand(
            clusterId, httpContext.Request.Method,
            k8sPath, headers, body, internalToken), ct);

        // ── Write result to HTTP response ──────────────────────────────────
        if (result.IsFailure)
        {
            await K8sStatusWriter.WriteAsync(httpContext, result.Error!);
            return;
        }

        var response = result.Value;
        httpContext.Response.StatusCode = response.StatusCode;
        foreach (var h in response.Headers)
            httpContext.Response.Headers.Append(h.Name, h.Value);

        if (response.Body.Length > 0)
            await httpContext.Response.Body.WriteAsync(response.Body, ct);
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
