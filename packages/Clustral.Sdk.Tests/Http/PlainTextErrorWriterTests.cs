using Clustral.Sdk.Http;
using Clustral.Sdk.Results;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Http;

/// <summary>
/// Covers every scenario <see cref="PlainTextErrorWriter"/> is used for:
/// every <see cref="ResultErrors"/> factory that flows through the proxy
/// path, every HTTP status the explicit-args overload handles, the
/// <c>X-Clustral-Error-Code</c> + <c>X-Correlation-Id</c> header contract,
/// and the Content-Type choice that makes kubectl's client-go discovery
/// fallback show our message instead of <c>"unknown"</c>.
/// </summary>
public class PlainTextErrorWriterTests(ITestOutputHelper output)
{
    // ── helpers ───────────────────────────────────────────────────────────

    private static DefaultHttpContext NewContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/proxy/00000000-0000-0000-0000-000000000000/api/v1/pods";
        return ctx;
    }

    private static async Task<string> ReadBodyAsync(DefaultHttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    //  Content-Type contract (the REASON plain text exists)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ContentType_IsTextPlain_SoKubectlFallbackUsesBodyVerbatim()
    {
        // kubectl's client-go checks `strings.HasPrefix(mediaType, "text/")`
        // in its `isTextResponse` helper. When true, `newUnstructuredResponseError`
        // uses `message = strings.TrimSpace(body)` and kubectl renders the
        // body verbatim after "error: ". If Content-Type were "application/json"
        // kubectl falls back to the hardcoded "unknown" instead.
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, ResultErrors.AuthenticationRequired());

        ctx.Response.ContentType.Should().StartWith("text/plain",
            "kubectl's client-go only reads the body as the error message " +
            "when Content-Type starts with 'text/' — JSON bodies trigger the 'unknown' fallback");
    }

    // ════════════════════════════════════════════════════════════════════
    //  X-Clustral-Error-Code header (machine-readable fallback)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ErrorCodeHeader_IsAlwaysSet_FromResultError()
    {
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, ResultErrors.NoRoleAssignment("alice@corp.com", Guid.Parse("44444444-4444-4444-4444-444444444444")));

        ctx.Response.Headers["X-Clustral-Error-Code"].ToString().Should().Be("NO_ROLE_ASSIGNMENT");
    }

    [Fact]
    public async Task ExplicitArgs_ErrorCodeHeader_IsSet()
    {
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, 401, "AUTHENTICATION_REQUIRED", "no token");

        ctx.Response.Headers["X-Clustral-Error-Code"].ToString().Should().Be("AUTHENTICATION_REQUIRED");
    }

    // ════════════════════════════════════════════════════════════════════
    //  X-Correlation-Id contract (always echoed)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EchoesIncomingCorrelationId()
    {
        var ctx = NewContext();
        ctx.Request.Headers["X-Correlation-Id"] = "caller-abc-123";

        await PlainTextErrorWriter.WriteAsync(ctx, ResultErrors.AuthenticationRequired());

        ctx.Response.Headers["X-Correlation-Id"].ToString().Should().Be("caller-abc-123");
    }

    [Fact]
    public async Task GeneratesCorrelationId_WhenRequestHasNone()
    {
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, ResultErrors.AuthenticationRequired());

        var generated = ctx.Response.Headers["X-Correlation-Id"].ToString();
        generated.Should().NotBeNullOrWhiteSpace();
        generated.Length.Should().BeGreaterThan(16, "generated GUIDs are 32 hex chars");
    }

    // ════════════════════════════════════════════════════════════════════
    //  Every ResultErrors factory → status code + body content
    // ════════════════════════════════════════════════════════════════════

    public static readonly TheoryData<ResultError, int, string, string[]> FactoryScenarios = new()
    {
        // factory                                             expected-status  expected-code               phrases-the-body-must-contain
        { ResultErrors.AuthenticationRequired(),               401,             "AUTHENTICATION_REQUIRED",  new[] { "Authentication required", "clustral kube login" } },
        { ResultErrors.MalformedToken("not a JWT"),            401,             "INVALID_TOKEN",            new[] { "malformed", "not a JWT", "clustral kube login" } },
        { ResultErrors.MissingSubjectClaim(),                  401,             "INVALID_TOKEN",            new[] { "'sub'", "clustral kube login" } },
        { ResultErrors.ClusterMismatch(Guid.Empty, Guid.Empty), 403,            "CLUSTER_MISMATCH",         new[] { "issued for cluster", "request is for cluster" } },
        { ResultErrors.CredentialRevoked(Guid.Empty),          401,             "CREDENTIAL_REVOKED",       new[] { "revoked", "clustral kube login" } },
        { ResultErrors.CredentialExpired(Guid.Empty, DateTimeOffset.UnixEpoch), 401, "CREDENTIAL_EXPIRED",  new[] { "expired", "clustral kube login" } },
        { ResultErrors.NoRoleAssignment("alice@corp.com", Guid.Parse("44444444-4444-4444-4444-444444444444")), 403,         "NO_ROLE_ASSIGNMENT",       new[] { "alice@corp.com", "44444444-4444-4444-4444-444444444444", "clustral access request", "clustral clusters list" } },
        { ResultErrors.InvalidClusterId("not-a-uuid"),         400,             "INVALID_CLUSTER_ID",       new[] { "not-a-uuid", "UUID", "clustral kube login" } },
        { ResultErrors.AgentNotConnected(Guid.Empty),          502,             "AGENT_NOT_CONNECTED",      new[] { "agent", "not currently connected", "clustral clusters list" } },
        { ResultErrors.TunnelTimeout(TimeSpan.FromSeconds(30)), 504,            "TUNNEL_TIMEOUT",           new[] { "did not respond", "30" } },
        { ResultErrors.TunnelError("stream reset"),            502,             "TUNNEL_ERROR",             new[] { "internal error", "stream reset", "retry" } },
        { ResultErrors.AgentError("API_UNREACHABLE", "connection refused"), 502, "AGENT_ERROR",             new[] { "API_UNREACHABLE", "connection refused", "cannot reach" } },
    };

    [Theory]
    [MemberData(nameof(FactoryScenarios))]
    public async Task WriteFromError_ProducesCorrectStatusCodeHeaderAndBody(
        ResultError error, int expectedStatus, string expectedCode, string[] requiredPhrases)
    {
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, error);
        var body = await ReadBodyAsync(ctx);

        output.WriteLine($"[{ctx.Response.StatusCode}] {body}");

        ctx.Response.StatusCode.Should().Be(expectedStatus);
        ctx.Response.Headers["X-Clustral-Error-Code"].ToString().Should().Be(expectedCode);
        foreach (var phrase in requiredPhrases)
            body.Should().Contain(phrase, $"message should guide the user ('{phrase}' was expected)");
    }

    // ════════════════════════════════════════════════════════════════════
    //  Status-code mapping (ResultErrorKind + proxy-specific Code overrides)
    // ════════════════════════════════════════════════════════════════════

    public static readonly TheoryData<ResultErrorKind, int> KindMappings = new()
    {
        { ResultErrorKind.NotFound,     404 },
        { ResultErrorKind.Unauthorized, 401 },
        { ResultErrorKind.Forbidden,    403 },
        { ResultErrorKind.Conflict,     409 },
        { ResultErrorKind.BadRequest,   400 },
        { ResultErrorKind.Validation,   422 },
        { ResultErrorKind.Internal,     500 },
    };

    [Theory]
    [MemberData(nameof(KindMappings))]
    public async Task WriteFromError_KindMapsToCorrectStatusCode(ResultErrorKind kind, int expectedStatus)
    {
        var ctx = NewContext();
        var error = new ResultError { Kind = kind, Code = "X", Message = "boom" };

        await PlainTextErrorWriter.WriteAsync(ctx, error);

        ctx.Response.StatusCode.Should().Be(expectedStatus);
    }

    public static readonly TheoryData<string, int> CodeOverrides = new()
    {
        { "TUNNEL_TIMEOUT",      504 },
        { "AGENT_NOT_CONNECTED", 502 },
        { "AGENT_ERROR",         502 },
        { "TUNNEL_ERROR",        502 },
    };

    [Theory]
    [MemberData(nameof(CodeOverrides))]
    public async Task WriteFromError_ProxyCodeOverridesKindMapping(string code, int expectedStatus)
    {
        // With Kind=Internal alone, the status would default to 500.
        // The Code override promotes it to the right 5xx.
        var ctx = NewContext();
        var error = new ResultError { Kind = ResultErrorKind.Internal, Code = code, Message = "boom" };

        await PlainTextErrorWriter.WriteAsync(ctx, error);

        ctx.Response.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public void MapStatusCode_ExposedForMiddleware()
    {
        PlainTextErrorWriter.MapStatusCode(ResultErrors.TunnelTimeout(TimeSpan.FromSeconds(1)))
            .Should().Be(504);
        PlainTextErrorWriter.MapStatusCode(ResultErrors.AgentNotConnected(Guid.NewGuid()))
            .Should().Be(502);
        PlainTextErrorWriter.MapStatusCode(ResultErrors.CredentialRevoked(Guid.NewGuid()))
            .Should().Be(401);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Explicit-args overload (gateway OnChallenge / OnRejected paths)
    // ════════════════════════════════════════════════════════════════════

    public static readonly TheoryData<int, string, string> ExplicitArgsScenarios = new()
    {
        { 400, "BAD_REQUEST",             "bad input"         },
        { 401, "AUTHENTICATION_REQUIRED", "no token supplied" },
        { 403, "FORBIDDEN",               "not allowed"       },
        { 404, "ROUTE_NOT_FOUND",         "no route matches"  },
        { 429, "RATE_LIMITED",            "slow down"         },
        { 500, "INTERNAL_ERROR",          "boom"              },
        { 502, "UPSTREAM_UNREACHABLE",    "service down"      },
        { 504, "UPSTREAM_TIMEOUT",        "service slow"      },
    };

    [Theory]
    [MemberData(nameof(ExplicitArgsScenarios))]
    public async Task WriteAsync_ExplicitArgs_ProducesPlainTextResponse(
        int status, string code, string message)
    {
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, status, code, message);
        var body = await ReadBodyAsync(ctx);

        ctx.Response.StatusCode.Should().Be(status);
        ctx.Response.ContentType.Should().StartWith("text/plain");
        ctx.Response.Headers["X-Clustral-Error-Code"].ToString().Should().Be(code);
        body.Should().Be(message);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Cross-cutting guarantees
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Body_IsSingleLine_ForKubectlRendering()
    {
        // kubectl prints the body verbatim after "error: ". Multi-line
        // messages would mangle the terminal output.
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx,
            ResultErrors.NoRoleAssignment("alice@corp.com", Guid.Parse("55555555-5555-5555-5555-555555555555")));
        var body = await ReadBodyAsync(ctx);

        output.WriteLine($"kubectl would render: error: {body}");
        body.Should().NotContain("\n");
        body.Should().NotContain("\r");
    }

    [Fact]
    public async Task Body_IsExactlyResultErrorMessage_NoWrappingNoEscaping()
    {
        // Plain text means plain text — no JSON escaping, no added wrapping.
        // The body bytes equal the ResultError.Message string.
        var ctx = NewContext();
        var error = new ResultError
        {
            Kind = ResultErrorKind.Forbidden, Code = "TEST",
            Message = "Exact text — with punctuation, apostrophes ' and unicode é.",
        };

        await PlainTextErrorWriter.WriteAsync(ctx, error);
        var body = await ReadBodyAsync(ctx);

        body.Should().Be("Exact text — with punctuation, apostrophes ' and unicode é.");
    }

    // ════════════════════════════════════════════════════════════════════
    //  Documentation discovery (RFC 8288 Link header)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkHeader_PointsAtResolvableDocumentationUrl()
    {
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, ResultErrors.AgentNotConnected(Guid.Empty));

        // RFC 8288 Link: <url>; rel="help"
        var link = ctx.Response.Headers["Link"].ToString();
        output.WriteLine($"Link: {link}");
        link.Should().Contain("https://docs.clustral.kube.it.com/errors/agent-not-connected");
        link.Should().Contain("rel=\"help\"");
    }

    [Fact]
    public async Task LinkHeader_SetOnExplicitArgsOverloadToo()
    {
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, 401, "AUTHENTICATION_REQUIRED", "no token");

        ctx.Response.Headers["Link"].ToString()
            .Should().Contain("https://docs.clustral.kube.it.com/errors/authentication-required");
    }

    [Fact]
    public async Task Writer_CompletesWithoutThrowing_ForAnyResultErrorKind()
    {
        var act = async () =>
        {
            foreach (var kind in Enum.GetValues<ResultErrorKind>())
            {
                var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
                ctx.Request.Path = "/api/proxy/x/y";
                var error = new ResultError { Kind = kind, Code = "CODE", Message = "msg" };
                await PlainTextErrorWriter.WriteAsync(ctx, error);
            }
        };
        await act.Should().NotThrowAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    //  Structured detail via headers (parity with RFC 7807 Problem Details)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FieldHeader_IsSetForValidationErrors()
    {
        var ctx = NewContext();
        var error = ResultError.Validation("must be >= 1", field: "page");

        await PlainTextErrorWriter.WriteAsync(ctx, error);

        ctx.Response.Headers["X-Clustral-Error-Field"].ToString().Should().Be("page");
    }

    [Fact]
    public async Task FieldHeader_IsNotSet_WhenFieldIsNull()
    {
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, ResultErrors.AuthenticationRequired());

        ctx.Response.Headers.ContainsKey("X-Clustral-Error-Field").Should().BeFalse();
    }

    [Fact]
    public async Task MetadataHeaders_AreSet_OnePerEntry()
    {
        var ctx = NewContext();
        var tokenCluster = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var requestedCluster = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var error = ResultErrors.ClusterMismatch(tokenCluster, requestedCluster);

        await PlainTextErrorWriter.WriteAsync(ctx, error);

        ctx.Response.Headers["X-Clustral-Error-Meta-tokenClusterId"].ToString()
            .Should().Be(tokenCluster.ToString());
        ctx.Response.Headers["X-Clustral-Error-Meta-requestedClusterId"].ToString()
            .Should().Be(requestedCluster.ToString());
    }

    [Fact]
    public async Task MetadataHeaders_FormatDateTimeOffsetAsIso8601()
    {
        var ctx = NewContext();
        var credId = Guid.NewGuid();
        var expiredAt = new DateTimeOffset(2026, 4, 13, 22, 0, 0, TimeSpan.Zero);

        await PlainTextErrorWriter.WriteAsync(ctx, ResultErrors.CredentialExpired(credId, expiredAt));

        var headerValue = ctx.Response.Headers["X-Clustral-Error-Meta-expiredAt"].ToString();
        output.WriteLine($"expiredAt header: {headerValue}");
        headerValue.Should().StartWith("2026-04-13T22:00:00");
    }

    [Fact]
    public async Task MetadataHeaders_FormatTimeSpanAsIso8601Duration()
    {
        var ctx = NewContext();
        var timeout = TimeSpan.FromMinutes(2);

        await PlainTextErrorWriter.WriteAsync(ctx, ResultErrors.TunnelTimeout(timeout));

        // TimeSpan "c" format is "[d.]hh:mm:ss[.fffffff]"
        ctx.Response.Headers["X-Clustral-Error-Meta-timeout"].ToString()
            .Should().Be("00:02:00");
    }

    [Fact]
    public async Task MetadataHeaders_StripCrAndLf_FromStringValues()
    {
        // HTTP headers must not contain CR or LF — CRLF-injection defence.
        var ctx = NewContext();
        var error = new ResultError
        {
            Kind = ResultErrorKind.Internal, Code = "BAD", Message = "oops",
            Metadata = new Dictionary<string, object>
            {
                ["dangerous"] = "line1\r\nInjected-Header: foo",
            },
        };

        await PlainTextErrorWriter.WriteAsync(ctx, error);

        var v = ctx.Response.Headers["X-Clustral-Error-Meta-dangerous"].ToString();
        v.Should().NotContain("\n");
        v.Should().NotContain("\r");
    }

    [Fact]
    public async Task MetadataHeaders_ReachParityWithRfc7807ProblemDetails()
    {
        // For every proxy-path ResultErrors factory that carries Metadata,
        // the same key/value pairs show up in headers. This is the
        // "feature parity with Result<T>" contract.
        var ctx = NewContext();
        var error = ResultErrors.NoRoleAssignment("alice@corp.com", Guid.Parse("55555555-5555-5555-5555-555555555555"));

        await PlainTextErrorWriter.WriteAsync(ctx, error);

        ctx.Response.Headers["X-Clustral-Error-Meta-user"].ToString().Should().Be("alice@corp.com");
        ctx.Response.Headers["X-Clustral-Error-Meta-clusterId"].ToString()
            .Should().Be("55555555-5555-5555-5555-555555555555");
        // Also has the code and field (from base ResultError properties).
        ctx.Response.Headers["X-Clustral-Error-Code"].ToString().Should().Be("NO_ROLE_ASSIGNMENT");
    }

    [Fact]
    public async Task ExplicitArgs_DoesNotEmitFieldOrMetadataHeaders()
    {
        // The explicit-args overload (used from gateway OnChallenge / OnRejected)
        // intentionally only sets Code + Correlation. It has no ResultError to
        // derive Field/Metadata from.
        var ctx = NewContext();

        await PlainTextErrorWriter.WriteAsync(ctx, 401, "AUTHENTICATION_REQUIRED", "no token");

        ctx.Response.Headers.ContainsKey("X-Clustral-Error-Field").Should().BeFalse();
        var metaHeaders = ctx.Response.Headers
            .Where(h => h.Key.StartsWith("X-Clustral-Error-Meta-", StringComparison.OrdinalIgnoreCase));
        metaHeaders.Should().BeEmpty();
    }

    [Fact]
    public async Task FullStructuredDetail_MatchesResultErrorExactly()
    {
        // Exhaustive check: every ResultError property that a consumer could
        // read via RFC 7807 is also readable via headers on the proxy path.
        var ctx = NewContext();
        var error = new ResultError
        {
            Kind = ResultErrorKind.Conflict,
            Code = "CUSTOM_CODE",
            Message = "some conflict",
            Field = "fooField",
            Metadata = new Dictionary<string, object>
            {
                ["resourceId"] = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                ["attemptCount"] = 7,
                ["enabled"] = true,
                ["note"] = "hello",
            },
        };

        await PlainTextErrorWriter.WriteAsync(ctx, error);
        var body = await ReadBodyAsync(ctx);

        body.Should().Be("some conflict");
        ctx.Response.Headers["X-Clustral-Error-Code"].ToString().Should().Be("CUSTOM_CODE");
        ctx.Response.Headers["X-Clustral-Error-Field"].ToString().Should().Be("fooField");
        ctx.Response.Headers["X-Clustral-Error-Meta-resourceId"].ToString()
            .Should().Be("33333333-3333-3333-3333-333333333333");
        ctx.Response.Headers["X-Clustral-Error-Meta-attemptCount"].ToString().Should().Be("7");
        ctx.Response.Headers["X-Clustral-Error-Meta-enabled"].ToString().Should().Be("True");
        ctx.Response.Headers["X-Clustral-Error-Meta-note"].ToString().Should().Be("hello");
    }
}
