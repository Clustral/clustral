using System.Diagnostics;
using System.Text.Json;
using Clustral.Sdk.Http;
using Clustral.Sdk.Results;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Http;

/// <summary>
/// Verifies W3C Trace Context integration. ASP.NET Core's hosting
/// diagnostics creates an <see cref="Activity"/> for every incoming HTTP
/// request (derived from <c>traceparent</c> if the caller sent one);
/// every writer must surface that trace ID on the response and in error
/// bodies so a caller's OpenTelemetry / Datadog / Honeycomb pipeline can
/// correlate without extra configuration.
/// </summary>
public class TraceContextIntegrationTests(ITestOutputHelper output) : IDisposable
{
    static TraceContextIntegrationTests()
    {
        // ASP.NET Core 5+ sets this globally; make it explicit for tests.
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
    }

    private readonly ActivitySource _source = new("Clustral.Tests");
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = _ => true,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    private Activity StartActivity()
    {
        ActivitySource.AddActivityListener(_listener);
        return _source.StartActivity("test")
               ?? throw new InvalidOperationException("expected ActivitySource to start an Activity once a listener is registered");
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    private static DefaultHttpContext NewContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/v1/test";
        return ctx;
    }

    [Fact]
    public async Task ProblemDetails_TraceIdExtension_MatchesActivityTraceId()
    {
        using var activity = StartActivity();
        var expectedTraceId = activity.TraceId.ToString();

        var ctx = NewContext();
        await ProblemDetailsWriter.WriteAsync(ctx, ResultError.Unauthorized("x"));

        ctx.Response.Body.Position = 0;
        var body = JsonDocument.Parse(
            await new StreamReader(ctx.Response.Body).ReadToEndAsync()).RootElement;

        var traceId = body.GetProperty("traceId").GetString();
        output.WriteLine($"Activity TraceId: {expectedTraceId}");
        output.WriteLine($"Body traceId:     {traceId}");
        traceId.Should().Be(expectedTraceId,
            "the RFC 7807 traceId extension must match the W3C Activity.TraceId " +
            "so OpenTelemetry / Datadog / Honeycomb can correlate without extra config");
    }

    [Fact]
    public async Task ProblemDetails_TraceId_IsThirtyTwoHex_W3CFormat()
    {
        using var activity = StartActivity();

        var ctx = NewContext();
        await ProblemDetailsWriter.WriteAsync(ctx, ResultError.Unauthorized("x"));

        ctx.Response.Body.Position = 0;
        var body = JsonDocument.Parse(
            await new StreamReader(ctx.Response.Body).ReadToEndAsync()).RootElement;

        var traceId = body.GetProperty("traceId").GetString();
        traceId.Should().HaveLength(32, "W3C trace IDs are 32 hex characters");
        traceId.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void ProblemDetails_TypeUrl_PointsAtResolvableDocs()
    {
        // RFC 7807 § 3: the `type` should be a URL that identifies the
        // problem — matching Stripe / Azure / Microsoft Graph convention.
        var error = ResultError.NotFound("CLUSTER_NOT_FOUND", "missing");
        var problem = ProblemDetailsWriter.BuildProblem(error);

        problem.Type.Should().StartWith("https://docs.clustral.kube.it.com/errors/");
        problem.Type.Should().EndWith("cluster-not-found");
        // Must be parseable as an absolute URI
        Uri.TryCreate(problem.Type, UriKind.Absolute, out _).Should().BeTrue();
    }
}
