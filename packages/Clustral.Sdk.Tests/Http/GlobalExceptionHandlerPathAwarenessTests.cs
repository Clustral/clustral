using Clustral.Sdk.Http;
using Clustral.Sdk.Results;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Http;

/// <summary>
/// The global exception handler must produce the same body shape the rest of
/// the stack produces for a given path: plain text on <c>/api/proxy/*</c>
/// (so kubectl renders it) and RFC 7807 everywhere else. Without this, an
/// exception leaking from the proxy handler would get written as JSON and
/// kubectl would fall back to "unknown".
/// </summary>
public class GlobalExceptionHandlerPathAwarenessTests(ITestOutputHelper output)
{
    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static async Task<DefaultHttpContext> RunAsync(string path, Exception thrown)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = path;

        var middleware = new GlobalExceptionHandlerMiddleware(
            next: _ => throw thrown,
            logger: NullLogger<GlobalExceptionHandlerMiddleware>.Instance,
            env: new FakeEnv("Production"));

        await middleware.InvokeAsync(ctx);

        ctx.Response.Body.Position = 0;
        return ctx;
    }

    private static async Task<string> BodyOf(DefaultHttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    }

    [Fact]
    public async Task ProxyPath_WritesPlainText_NotJson()
    {
        var ctx = await RunAsync("/api/proxy/abc-123/api/v1/pods", new InvalidOperationException("something broke"));
        var body = await BodyOf(ctx);

        output.WriteLine($"Content-Type: {ctx.Response.ContentType}");
        output.WriteLine(body);

        ctx.Response.ContentType.Should().StartWith("text/plain",
            "kubectl only decodes the body as the error message when Content-Type is text/*");
        body.Should().NotStartWith("{");
    }

    [Fact]
    public async Task ProxyPath_EmitsErrorCodeHeader()
    {
        // Unhandled InvalidOperationException maps to 422 Unprocessable via ClassifyException.
        var ctx = await RunAsync("/api/proxy/abc-123/api/v1/pods",
            new InvalidOperationException("bad state"));

        ctx.Response.Headers.ContainsKey(PlainTextErrorWriter.ErrorCodeHeader).Should().BeTrue();
        ctx.Response.Headers[PlainTextErrorWriter.ErrorCodeHeader].ToString()
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NonProxyPath_StillWritesProblemJson()
    {
        var ctx = await RunAsync("/api/v1/clusters", new InvalidOperationException("rest error"));

        ctx.Response.ContentType.Should().StartWith("application/problem+json");
        var body = await BodyOf(ctx);
        body.Should().StartWith("{");
    }

    [Fact]
    public async Task ProxyPath_ResultFailureException_KeepsResultErrorMessage()
    {
        // A Result<T>.ThrowIfFailed() can propagate a ResultFailureException
        // out to the middleware. The plain-text writer must still carry the
        // ResultError's code + message.
        var error = ResultErrors.AgentNotConnected(Guid.Parse("77777777-7777-7777-7777-777777777777"));
        var ctx = await RunAsync("/api/proxy/77777777-7777-7777-7777-777777777777/api/v1/pods",
            new ResultFailureException(error));

        ctx.Response.StatusCode.Should().Be(500);  // maps from Kind=Internal
        ctx.Response.Headers[PlainTextErrorWriter.ErrorCodeHeader].ToString()
            .Should().Be("AGENT_NOT_CONNECTED");
        var body = await BodyOf(ctx);
        body.Should().Contain("77777777-7777-7777-7777-777777777777");
        body.Should().Contain("agent");
    }

    [Fact]
    public async Task ProxyPath_CorrelationIdIsEchoed()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/proxy/abc/api/v1/pods";
        ctx.Request.Headers["X-Correlation-Id"] = "caller-xyz";

        var middleware = new GlobalExceptionHandlerMiddleware(
            next: _ => throw new InvalidOperationException("fail"),
            logger: NullLogger<GlobalExceptionHandlerMiddleware>.Instance,
            env: new FakeEnv("Production"));

        await middleware.InvokeAsync(ctx);

        ctx.Response.Headers["X-Correlation-Id"].ToString().Should().Be("caller-xyz");
    }
}
