using System.Diagnostics;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Services;
using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Protos;
using Clustral.V1;
using Google.Protobuf;
using MediatR;
using Microsoft.Extensions.Options;

namespace Clustral.ControlPlane.Api;

/// <summary>
/// ASP.NET Core middleware that proxies kubectl HTTP requests through the
/// agent tunnel. Delegates authentication and impersonation resolution
/// to domain services for testability.
///
/// Path format: <c>/proxy/{clusterId}/{k8s-api-path}</c>
/// </summary>
public sealed class KubectlProxyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value ?? "";
        var ct = httpContext.RequestAborted;

        // Match /api/proxy/{clusterId}/... or /proxy/{clusterId}/...
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

        var sw = Stopwatch.StartNew();
        var services = httpContext.RequestServices;
        var logger = services.GetRequiredService<ILogger<KubectlProxyMiddleware>>();
        var proxyOptions = services.GetRequiredService<IOptions<ProxyOptions>>().Value;

        // ── Parse path ──────────────────────────────────────────────────────
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

        // ── 1. Authenticate ─────────────────────────────────────────────────
        var bearerToken = ExtractBearerToken(httpContext.Request);
        if (bearerToken is null)
        {
            httpContext.Response.StatusCode = 401;
            await httpContext.Response.WriteAsync("Authorization: Bearer token required.", cancellationToken: ct);
            return;
        }

        var proxyAuth = services.GetRequiredService<ProxyAuthService>();
        var authResult = await proxyAuth.AuthenticateAsync(bearerToken, clusterId, ct);
        if (authResult.IsFailure)
        {
            httpContext.Response.StatusCode = authResult.Error!.Kind switch
            {
                Sdk.Results.ResultErrorKind.Forbidden => 403,
                _ => 401,
            };
            await httpContext.Response.WriteAsync(authResult.Error.Message, cancellationToken: ct);
            return;
        }

        var identity = authResult.Value;

        // ── 2. Resolve impersonation ────────────────────────────────────────
        var impersonation = services.GetRequiredService<ImpersonationResolver>();
        var impResult = await impersonation.ResolveAsync(identity.UserId, clusterId, ct);
        if (impResult.IsFailure)
        {
            httpContext.Response.StatusCode = 403;
            await httpContext.Response.WriteAsync(impResult.Error!.Message, cancellationToken: ct);
            return;
        }

        var imp = impResult.Value;

        // ── 3. Find tunnel session ──────────────────────────────────────────
        var sessions = services.GetRequiredService<TunnelSessionManager>();
        var session = sessions.GetSession(clusterId);
        if (session is null)
        {
            httpContext.Response.StatusCode = 502;
            await httpContext.Response.WriteAsync("Cluster agent is not connected.", cancellationToken: ct);
            return;
        }

        // ── 4. Build HttpRequestFrame ───────────────────────────────────────
        var requestId = Guid.NewGuid().ToString("N");
        var head = new HttpRequestHead
        {
            Method = httpContext.Request.Method,
            Path = k8sPath,
        };

        foreach (var (name, values) in httpContext.Request.Headers)
        {
            if (IsHopByHop(name) ||
                name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                continue;
            head.Headers.Add(new HttpHeader { Name = name, Value = string.Join(", ", values!) });
        }

        // Inject impersonation headers.
        head.Headers.Add(new HttpHeader { Name = "X-Clustral-Impersonate-User", Value = imp.User });
        foreach (var group in imp.Groups)
            head.Headers.Add(new HttpHeader { Name = "X-Clustral-Impersonate-Group", Value = group });

        ByteString bodyBytes = ByteString.Empty;
        if (httpContext.Request.ContentLength > 0 ||
            httpContext.Request.Method is "POST" or "PUT" or "PATCH")
        {
            using var ms = new MemoryStream();
            await httpContext.Request.Body.CopyToAsync(ms, ct);
            bodyBytes = ByteString.CopyFrom(ms.ToArray());
        }

        var frame = new HttpRequestFrame
        {
            RequestId = requestId,
            Head = head,
            BodyChunk = bodyBytes,
            EndOfBody = true,
        };

        // ── 5. Proxy through tunnel with timeout ────────────────────────────
        HttpResponseFrame responseFrame;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(proxyOptions.TunnelTimeout);
            responseFrame = await session.ProxyAsync(frame, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Tunnel timeout (not client disconnect).
            httpContext.Response.StatusCode = 504;
            await httpContext.Response.WriteAsync("Gateway timeout — agent did not respond.", cancellationToken: ct);
            LogProxyRequest(logger, httpContext.Request.Method, clusterId, k8sPath, 504, sw, identity);
            return;
        }
        catch (OperationCanceledException)
        {
            // Client disconnected.
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tunnel proxy error for cluster {ClusterId}", clusterId);
            httpContext.Response.StatusCode = 502;
            await httpContext.Response.WriteAsync("Tunnel proxy error.", cancellationToken: ct);
            LogProxyRequest(logger, httpContext.Request.Method, clusterId, k8sPath, 502, sw, identity);
            return;
        }

        // ── 6. Handle tunnel-level errors ───────────────────────────────────
        if (responseFrame.Error is { } tunnelError)
        {
            httpContext.Response.StatusCode = 502;
            await httpContext.Response.WriteAsync(
                $"Agent error ({tunnelError.Code}): {tunnelError.Message}", cancellationToken: ct);
            LogProxyRequest(logger, httpContext.Request.Method, clusterId, k8sPath, 502, sw, identity);
            return;
        }

        // ── 7. Write HTTP response ──────────────────────────────────────────
        var responseHead = responseFrame.Head;
        if (responseHead is not null)
        {
            httpContext.Response.StatusCode = responseHead.StatusCode;
            foreach (var h in responseHead.Headers)
            {
                if (IsHopByHop(h.Name)) continue;
                httpContext.Response.Headers.Append(h.Name, h.Value);
            }
        }

        if (responseFrame.BodyChunk.Length > 0)
            await httpContext.Response.Body.WriteAsync(responseFrame.BodyChunk.Memory, ct);

        // ── 8. Structured log + audit event ─────────────────────────────────
        var statusCode = httpContext.Response.StatusCode;
        LogProxyRequest(logger, httpContext.Request.Method, clusterId, k8sPath, statusCode, sw, identity);

        try
        {
            var mediator = services.GetRequiredService<IMediator>();
            await mediator.Publish(new ProxyRequestCompleted(
                clusterId, identity.UserId, identity.CredentialId,
                httpContext.Request.Method, k8sPath, statusCode, sw.Elapsed.TotalMilliseconds), ct);
        }
        catch
        {
            // Don't fail the proxy response if event dispatch fails.
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static void LogProxyRequest(
        ILogger logger, string method, Guid clusterId, string k8sPath,
        int statusCode, Stopwatch sw, ProxyIdentity identity)
    {
        logger.LogInformation(
            "Proxy {Method} {ClusterId} {K8sPath} → {StatusCode} in {Duration}ms " +
            "[user={UserId} credential={CredentialId}]",
            method, clusterId, k8sPath, statusCode, sw.Elapsed.TotalMilliseconds,
            identity.UserId, identity.CredentialId);
    }

    private static string? ExtractBearerToken(HttpRequest request)
    {
        var auth = request.Headers.Authorization.FirstOrDefault();
        if (auth is null || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return auth["Bearer ".Length..].Trim();
    }

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailers", "Transfer-Encoding", "Upgrade",
    };

    private static bool IsHopByHop(string name) => HopByHopHeaders.Contains(name);
}
