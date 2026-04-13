using System.Text.Json;
using Clustral.Sdk.Http;
using Clustral.Sdk.Results;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Http;

public class ProblemDetailsWriterTests(ITestOutputHelper output)
{
    private static DefaultHttpContext NewContext(string? incomingCorrelationId = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/v1/test";
        if (incomingCorrelationId is not null)
            ctx.Request.Headers["X-Correlation-Id"] = incomingCorrelationId;
        return ctx;
    }

    private static async Task<(int StatusCode, string ContentType, JsonElement Body, string CorrelationId)>
        ReadResponseAsync(DefaultHttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        var json = JsonDocument.Parse(body).RootElement;
        return (
            ctx.Response.StatusCode,
            ctx.Response.ContentType ?? "",
            json,
            ctx.Response.Headers["X-Correlation-Id"].ToString());
    }

    [Fact]
    public async Task WriteFromError_ShapesRfc7807()
    {
        var ctx = NewContext();
        var error = ResultError.NotFound("ROLE_NOT_FOUND", "Role 'admin' not found.", "roleId");

        await ProblemDetailsWriter.WriteAsync(ctx, error);
        var (status, contentType, body, _) = await ReadResponseAsync(ctx);

        output.WriteLine($"Status={status} ContentType={contentType}");
        status.Should().Be(404);
        contentType.Should().StartWith("application/problem+json");

        // RFC 7807 `type` is a resolvable URL pointing at the error docs,
        // with the code in kebab-case (ROLE_NOT_FOUND → role-not-found).
        body.GetProperty("type").GetString().Should().Be("https://docs.clustral.kube.it.com/errors/role-not-found");
        body.GetProperty("title").GetString().Should().Be("NotFound");
        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("detail").GetString().Should().Be("Role 'admin' not found.");
        body.GetProperty("code").GetString().Should().Be("ROLE_NOT_FOUND");
        body.GetProperty("field").GetString().Should().Be("roleId");
    }

    [Fact]
    public async Task WriteAsync_ExplicitStatus_Produces401WithCode()
    {
        var ctx = NewContext();

        await ProblemDetailsWriter.WriteAsync(ctx, 401, "AUTHENTICATION_REQUIRED",
            "Authentication required.");
        var (status, _, body, _) = await ReadResponseAsync(ctx);

        status.Should().Be(401);
        body.GetProperty("title").GetString().Should().Be("Unauthorized");
        body.GetProperty("code").GetString().Should().Be("AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async Task EchoesCorrelationId_FromRequestHeader()
    {
        var ctx = NewContext(incomingCorrelationId: "abc-123");

        await ProblemDetailsWriter.WriteAsync(ctx, ResultError.Unauthorized("nope"));
        var (_, _, _, correlationId) = await ReadResponseAsync(ctx);

        output.WriteLine($"CorrelationId echoed: {correlationId}");
        correlationId.Should().Be("abc-123");
    }

    [Fact]
    public async Task GeneratesCorrelationId_WhenRequestHeaderMissing()
    {
        var ctx = NewContext();

        await ProblemDetailsWriter.WriteAsync(ctx, ResultError.Unauthorized("nope"));
        var (_, _, _, correlationId) = await ReadResponseAsync(ctx);

        correlationId.Should().NotBeNullOrWhiteSpace();
        correlationId.Length.Should().BeGreaterThan(16);
    }

    [Fact]
    public async Task MetadataAndTraceId_SurfaceAsExtensions()
    {
        var ctx = NewContext();
        var error = ResultError.Conflict("DUP", "Already exists.",
            new Dictionary<string, object> { ["existingId"] = "abc-123" });

        await ProblemDetailsWriter.WriteAsync(ctx, error);
        var (_, _, body, _) = await ReadResponseAsync(ctx);

        body.GetProperty("existingId").GetString().Should().Be("abc-123");
        body.TryGetProperty("traceId", out _).Should().BeTrue("writer should always populate traceId");
    }

    [Fact]
    public void BuildProblem_Unauthorized_Maps401()
    {
        var error = ResultError.Unauthorized("no token");
        var problem = ProblemDetailsWriter.BuildProblem(error, "/x");
        problem.Status.Should().Be(401);
        problem.Title.Should().Be("Unauthorized");
        problem.Extensions["code"].Should().Be("UNAUTHORIZED");
    }
}
