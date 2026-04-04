using System.Security.Cryptography;
using System.Text;
using DomainCredentialKind = Clustral.ControlPlane.Domain.CredentialKind;
using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Protos;
using Clustral.V1;
using Google.Protobuf;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Api;

/// <summary>
/// ASP.NET Core middleware that proxies kubectl HTTP requests through the
/// agent tunnel.
///
/// Path format: <c>/proxy/{clusterId}/{k8s-api-path}</c>
///
/// Authentication: the <c>Authorization: Bearer {token}</c> header must
/// contain a valid Clustral kubeconfig credential (issued by
/// <c>POST /api/v1/auth/kubeconfig-credential</c>).
///
/// Flow:
/// <list type="number">
///   <item>Validate the bearer token against the access_tokens collection.</item>
///   <item>Look up the <see cref="TunnelSession"/> for the cluster.</item>
///   <item>Build an <see cref="HttpRequestFrame"/> from the incoming HTTP request.</item>
///   <item>Send it through the tunnel and await the <see cref="HttpResponseFrame"/>.</item>
///   <item>Write the response back to the HTTP caller (kubectl).</item>
/// </list>
/// </summary>
public sealed class KubectlProxyMiddleware
{
    private readonly RequestDelegate _next;

    public KubectlProxyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value ?? "";

        // Only handle /proxy/{clusterId}/...
        if (!path.StartsWith("/proxy/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(httpContext);
            return;
        }

        // Parse: /proxy/{clusterId}/api/v1/pods → clusterId + /api/v1/pods
        var afterProxy = path["/proxy/".Length..];
        var slashIdx = afterProxy.IndexOf('/');

        string clusterIdStr;
        string k8sPath;

        if (slashIdx < 0)
        {
            clusterIdStr = afterProxy;
            k8sPath = "/";
        }
        else
        {
            clusterIdStr = afterProxy[..slashIdx];
            k8sPath = afterProxy[slashIdx..];
        }

        if (!Guid.TryParse(clusterIdStr, out var clusterId))
        {
            httpContext.Response.StatusCode = 400;
            await httpContext.Response.WriteAsync("Invalid cluster ID.");
            return;
        }

        // Append query string.
        if (httpContext.Request.QueryString.HasValue)
            k8sPath += httpContext.Request.QueryString.Value;

        var ct = httpContext.RequestAborted;

        // ── 1. Authenticate ──────────────────────────────────────────────────
        var db = httpContext.RequestServices.GetRequiredService<ClustralDb>();
        var bearerToken = ExtractBearerToken(httpContext.Request);

        if (bearerToken is null)
        {
            httpContext.Response.StatusCode = 401;
            await httpContext.Response.WriteAsync("Authorization: Bearer token required.");
            return;
        }

        var tokenHash = HashToken(bearerToken);
        var credential = await db.AccessTokens
            .Find(t => t.TokenHash == tokenHash && t.Kind == DomainCredentialKind.UserKubeconfig)
            .FirstOrDefaultAsync(ct);

        if (credential is null || !credential.IsValid)
        {
            httpContext.Response.StatusCode = 401;
            await httpContext.Response.WriteAsync("Invalid or expired credential.");
            return;
        }

        if (credential.ClusterId != clusterId)
        {
            httpContext.Response.StatusCode = 403;
            await httpContext.Response.WriteAsync("Credential is not valid for this cluster.");
            return;
        }

        // ── 2. Find tunnel session ───────────────────────────────────────────
        var sessions = httpContext.RequestServices.GetRequiredService<TunnelSessionManager>();
        var session = sessions.GetSession(clusterId);

        if (session is null)
        {
            httpContext.Response.StatusCode = 502;
            await httpContext.Response.WriteAsync("Cluster agent is not connected.");
            return;
        }

        // ── 3. Build HttpRequestFrame ────────────────────────────────────────
        var requestId = Guid.NewGuid().ToString("N");

        var head = new HttpRequestHead
        {
            Method = httpContext.Request.Method,
            Path   = k8sPath,
        };

        // Forward headers (skip hop-by-hop and Authorization).
        foreach (var (name, values) in httpContext.Request.Headers)
        {
            if (IsHopByHop(name) ||
                name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                continue;

            head.Headers.Add(new HttpHeader
            {
                Name  = name,
                Value = string.Join(", ", values!),
            });
        }

        // Read request body.
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
            Head      = head,
            BodyChunk = bodyBytes,
            EndOfBody = true,
        };

        // ── 4. Proxy through tunnel ──────────────────────────────────────────
        HttpResponseFrame responseFrame;
        try
        {
            responseFrame = await session.ProxyAsync(frame, ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected.
            return;
        }
        catch (Exception)
        {
            httpContext.Response.StatusCode = 502;
            await httpContext.Response.WriteAsync("Tunnel proxy error.");
            return;
        }

        // ── 5. Handle tunnel-level errors ────────────────────────────────────
        if (responseFrame.Error is { } tunnelError)
        {
            httpContext.Response.StatusCode = 502;
            await httpContext.Response.WriteAsync(
                $"Agent error ({tunnelError.Code}): {tunnelError.Message}");
            return;
        }

        // ── 6. Write HTTP response ───────────────────────────────────────────
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
        {
            await httpContext.Response.Body.WriteAsync(
                responseFrame.BodyChunk.Memory, ct);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static string? ExtractBearerToken(HttpRequest request)
    {
        var auth = request.Headers.Authorization.FirstOrDefault();
        if (auth is null || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return auth["Bearer ".Length..].Trim();
    }

    private static string HashToken(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailers", "Transfer-Encoding", "Upgrade",
    };

    private static bool IsHopByHop(string name) => HopByHopHeaders.Contains(name);
}
