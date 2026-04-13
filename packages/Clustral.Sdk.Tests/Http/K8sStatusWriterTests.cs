using System.Text.Json;
using Clustral.Sdk.Http;
using Clustral.Sdk.Results;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Http;

public class K8sStatusWriterTests(ITestOutputHelper output)
{
    private static DefaultHttpContext NewContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/proxy/00000000-0000-0000-0000-000000000000/api/v1/pods";
        return ctx;
    }

    private static async Task<JsonElement> ReadBodyAsync(DefaultHttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task WriteFromError_UnauthorizedKind_ProducesV1StatusUnauthorized()
    {
        var ctx = NewContext();
        var error = ResultErrors.AuthenticationRequired();

        await K8sStatusWriter.WriteAsync(ctx, error);
        var body = await ReadBodyAsync(ctx);

        output.WriteLine($"status={ctx.Response.StatusCode} content-type={ctx.Response.ContentType}");

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Response.ContentType.Should().Be("application/json");

        body.GetProperty("kind").GetString().Should().Be("Status");
        body.GetProperty("apiVersion").GetString().Should().Be("v1");
        body.GetProperty("status").GetString().Should().Be("Failure");
        body.GetProperty("reason").GetString().Should().Be("Unauthorized");
        body.GetProperty("code").GetInt32().Should().Be(401);
        body.GetProperty("message").GetString()
            .Should().Contain("Authentication required");

        // Clustral-specific error code lives in details.causes[0].reason
        var cause = body.GetProperty("details").GetProperty("causes")[0];
        cause.GetProperty("reason").GetString().Should().Be("AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async Task WriteFromError_ClusterMismatch_Produces403Forbidden()
    {
        var ctx = NewContext();
        var tokenCluster = Guid.NewGuid();
        var requested = Guid.NewGuid();

        await K8sStatusWriter.WriteAsync(ctx,
            ResultErrors.ClusterMismatch(tokenCluster, requested));
        var body = await ReadBodyAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        body.GetProperty("reason").GetString().Should().Be("Forbidden");
        body.GetProperty("details").GetProperty("causes")[0]
            .GetProperty("reason").GetString().Should().Be("CLUSTER_MISMATCH");
        body.GetProperty("message").GetString()
            .Should().Contain(tokenCluster.ToString())
            .And.Contain(requested.ToString());
    }

    [Fact]
    public async Task WriteFromError_AgentNotConnected_Produces502ServiceUnavailable()
    {
        var ctx = NewContext();
        var clusterId = Guid.NewGuid();

        await K8sStatusWriter.WriteAsync(ctx,
            ResultErrors.AgentNotConnected(clusterId));
        var body = await ReadBodyAsync(ctx);

        ctx.Response.StatusCode.Should().Be(502);
        body.GetProperty("reason").GetString().Should().Be("ServiceUnavailable");
        body.GetProperty("details").GetProperty("causes")[0]
            .GetProperty("reason").GetString().Should().Be("AGENT_NOT_CONNECTED");
    }

    [Fact]
    public async Task WriteFromError_TunnelTimeout_Produces504Timeout()
    {
        var ctx = NewContext();

        await K8sStatusWriter.WriteAsync(ctx,
            ResultErrors.TunnelTimeout(TimeSpan.FromSeconds(30)));
        var body = await ReadBodyAsync(ctx);

        ctx.Response.StatusCode.Should().Be(504);
        body.GetProperty("reason").GetString().Should().Be("Timeout");
        body.GetProperty("details").GetProperty("causes")[0]
            .GetProperty("reason").GetString().Should().Be("TUNNEL_TIMEOUT");
    }

    [Fact]
    public async Task WriteFromError_InvalidClusterId_Produces400BadRequest()
    {
        var ctx = NewContext();

        await K8sStatusWriter.WriteAsync(ctx,
            ResultErrors.InvalidClusterId("not-a-uuid"));
        var body = await ReadBodyAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        body.GetProperty("reason").GetString().Should().Be("BadRequest");
        body.GetProperty("details").GetProperty("causes")[0]
            .GetProperty("reason").GetString().Should().Be("INVALID_CLUSTER_ID");
    }

    [Fact]
    public async Task WriteAsync_ExplicitArgs_Produces403Forbidden()
    {
        var ctx = NewContext();

        await K8sStatusWriter.WriteAsync(ctx,
            statusCode: 403,
            reason: "Forbidden",
            message: "Not allowed.",
            causeReason: "FORBIDDEN");
        var body = await ReadBodyAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        body.GetProperty("reason").GetString().Should().Be("Forbidden");
        body.GetProperty("details").GetProperty("causes")[0]
            .GetProperty("reason").GetString().Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task EchoesCorrelationId()
    {
        var ctx = NewContext();
        ctx.Request.Headers["X-Correlation-Id"] = "test-correlation-xyz";

        await K8sStatusWriter.WriteAsync(ctx, ResultErrors.AuthenticationRequired());

        ctx.Response.Headers["X-Correlation-Id"].ToString().Should().Be("test-correlation-xyz");
    }

    [Fact]
    public async Task MessageIsKubectlReadable()
    {
        var ctx = NewContext();

        await K8sStatusWriter.WriteAsync(ctx,
            ResultErrors.NoRoleAssignment("alice@corp.com", "prod-cluster"));
        var body = await ReadBodyAsync(ctx);

        // kubectl prints `status.message` verbatim as "error: <message>".
        // The message must stay a clean, single-line human-readable string.
        var message = body.GetProperty("message").GetString();
        output.WriteLine($"kubectl would render: error: {message}");
        message.Should().NotContain("\n");
        message.Should().Contain("alice@corp.com");
        message.Should().Contain("prod-cluster");
    }
}
