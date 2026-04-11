using System.Diagnostics;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Services;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Protos;
using Clustral.Sdk.Results;
using Clustral.V1;
using Google.Protobuf;
using MediatR;
using Microsoft.Extensions.Options;

namespace Clustral.ControlPlane.Features.Proxy.Commands;

// ── Command ─────────────────────────────────────────────────────────────────

/// <summary>
/// Proxies a kubectl HTTP request through the agent tunnel.
/// The middleware extracts these values from <c>HttpContext</c> and sends
/// this command — all business logic lives in the handler.
/// </summary>
public sealed record ProxyKubectlRequestCommand(
    Guid ClusterId,
    string BearerToken,
    string Method,
    string K8sPath,
    IReadOnlyList<ProxyHeader> ForwardHeaders,
    byte[] Body,
    string? InternalToken = null) : ICommand<Result<ProxyKubectlResponse>>;

/// <summary>HTTP header forwarded from the client request.</summary>
public sealed record ProxyHeader(string Name, string Value);

// ── Response ────────────────────────────────────────────────────────────────

/// <summary>
/// Domain result of a proxied kubectl request. The middleware writes this
/// back to <c>HttpContext.Response</c>.
/// </summary>
public sealed record ProxyKubectlResponse(
    int StatusCode,
    IReadOnlyList<ProxyHeader> Headers,
    byte[] Body);

// ── Handler ─────────────────────────────────────────────────────────────────

public sealed class ProxyKubectlRequestHandler(
    ProxyAuthService proxyAuth,
    ImpersonationResolver impersonation,
    TunnelSessionManager sessions,
    IOptions<ProxyOptions> proxyOptions,
    IMediator mediator,
    ILogger<ProxyKubectlRequestHandler> logger)
    : IRequestHandler<ProxyKubectlRequestCommand, Result<ProxyKubectlResponse>>
{
    public async Task<Result<ProxyKubectlResponse>> Handle(
        ProxyKubectlRequestCommand request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // ── 1. Authenticate ────────────────────────────────────────────────
        var authResult = await proxyAuth.AuthenticateAsync(
            request.BearerToken, request.ClusterId, ct, request.InternalToken);
        if (authResult.IsFailure)
        {
            PublishAccessDeniedEvent(request, null, authResult.Error!.Message);
            return authResult.Error!;
        }

        var identity = authResult.Value;

        // ── 2. Resolve impersonation ───────────────────────────────────────
        var impResult = await impersonation.ResolveAsync(
            identity.UserId, request.ClusterId, ct);
        if (impResult.IsFailure)
        {
            PublishAccessDeniedEvent(request, identity.UserId, impResult.Error!.Message);
            return impResult.Error!;
        }

        var imp = impResult.Value;

        // ── 3. Find tunnel session ─────────────────────────────────────────
        var session = sessions.GetSession(request.ClusterId);
        if (session is null)
            return new ResultError
            {
                Kind = ResultErrorKind.Internal, Code = "AGENT_NOT_CONNECTED",
                Message = "Cluster agent is not connected.",
            };

        // ── 4. Build HttpRequestFrame ──────────────────────────────────────
        var head = new HttpRequestHead
        {
            Method = request.Method,
            Path = request.K8sPath,
        };

        foreach (var h in request.ForwardHeaders)
            head.Headers.Add(new HttpHeader { Name = h.Name, Value = h.Value });

        head.Headers.Add(new HttpHeader
            { Name = "X-Clustral-Impersonate-User", Value = imp.User });
        foreach (var group in imp.Groups)
            head.Headers.Add(new HttpHeader
                { Name = "X-Clustral-Impersonate-Group", Value = group });

        var frame = new HttpRequestFrame
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Head = head,
            BodyChunk = request.Body.Length > 0
                ? ByteString.CopyFrom(request.Body)
                : ByteString.Empty,
            EndOfBody = true,
        };

        // ── 5. Proxy through tunnel with timeout ───────────────────────────
        HttpResponseFrame responseFrame;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(proxyOptions.Value.TunnelTimeout);
            responseFrame = await session.ProxyAsync(frame, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            PublishAuditEvent(identity, request, 504, sw);
            return new ResultError
                {
                    Kind = ResultErrorKind.Internal, Code = "GATEWAY_TIMEOUT",
                    Message = "Gateway timeout — agent did not respond.",
                };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tunnel proxy error for cluster {ClusterId}",
                request.ClusterId);
            PublishAuditEvent(identity, request, 502, sw);
            return ResultError.Internal("Tunnel proxy error.");
        }

        // ── 6. Handle tunnel-level errors ──────────────────────────────────
        if (responseFrame.Error is { } tunnelError)
        {
            PublishAuditEvent(identity, request, 502, sw);
            return new ResultError
            {
                Kind = ResultErrorKind.Internal, Code = "AGENT_ERROR",
                Message = $"Agent error ({tunnelError.Code}): {tunnelError.Message}",
            };
        }

        // ── 7. Build domain response ───────────────────────────────────────
        var responseHead = responseFrame.Head;
        var statusCode = responseHead?.StatusCode ?? 200;
        var headers = new List<ProxyHeader>();
        if (responseHead is not null)
        {
            foreach (var h in responseHead.Headers)
            {
                if (!IsHopByHop(h.Name))
                    headers.Add(new ProxyHeader(h.Name, h.Value));
            }
        }

        var body = responseFrame.BodyChunk.Length > 0
            ? responseFrame.BodyChunk.ToByteArray()
            : [];

        // ── 8. Publish audit event ─────────────────────────────────────────
        PublishAuditEvent(identity, request, statusCode, sw);

        return new ProxyKubectlResponse(statusCode, headers, body);
    }

    /// <summary>Max request body size stored in audit logs (8 KB).</summary>
    private const int MaxAuditBodyBytes = 8 * 1024;

    private void PublishAuditEvent(
        ProxyIdentity identity,
        ProxyKubectlRequestCommand request,
        int statusCode,
        Stopwatch sw)
    {
        // Fire-and-forget — don't fail the proxy response if event dispatch fails.
        var requestBody = TruncateBody(request.Body);
        _ = Task.Run(async () =>
        {
            try
            {
                await mediator.Publish(new ProxyRequestCompleted(
                    request.ClusterId, identity.UserId, identity.CredentialId,
                    request.Method, request.K8sPath, statusCode,
                    sw.Elapsed.TotalMilliseconds, requestBody));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish proxy audit event");
            }
        });
    }

    private static string? TruncateBody(byte[] body)
    {
        if (body.Length == 0) return null;
        if (body.Length <= MaxAuditBodyBytes)
            return System.Text.Encoding.UTF8.GetString(body);
        return string.Concat(
            System.Text.Encoding.UTF8.GetString(body, 0, MaxAuditBodyBytes),
            $"... (truncated, {body.Length} bytes total)");
    }

    private void PublishAccessDeniedEvent(
        ProxyKubectlRequestCommand request, Guid? userId, string reason)
    {
        // Fire-and-forget — don't delay the 401/403 response for audit dispatch.
        _ = Task.Run(async () =>
        {
            try
            {
                await mediator.Publish(new ProxyAccessDenied(
                    request.ClusterId, userId,
                    request.Method, request.K8sPath, reason));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish proxy access denied event");
            }
        });
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
