using System.Net.Sockets;
using Clustral.Cli.Ui;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class CliErrorsTests(ITestOutputHelper output)
{
    // ── WriteHttpError ──────────────────────────────────────────────────────

    [Fact]
    public void WriteHttpError_401_ShowsUnauthorized()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 401, "");

        output.WriteLine("=== HTTP 401 ===");
        output.WriteLine(console.Output);

        Assert.Contains("401", console.Output);
        Assert.Contains("Unauthorized", console.Output);
        Assert.Contains("Error", console.Output);
    }

    [Fact]
    public void WriteHttpError_404_ParsesProblemDetails()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 404,
            """{"detail":"Role 'admin' not found.","code":"ROLE_NOT_FOUND"}""");

        output.WriteLine("=== HTTP 404 (Problem Details) ===");
        output.WriteLine(console.Output);

        Assert.Contains("404", console.Output);
        Assert.Contains("Role 'admin' not found.", console.Output);
        Assert.Contains("ROLE_NOT_FOUND", console.Output);
    }

    [Fact]
    public void WriteHttpError_409_ShowsConflict()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 409,
            """{"detail":"Cluster 'prod' already exists.","code":"DUPLICATE_CLUSTER_NAME"}""");

        output.WriteLine("=== HTTP 409 ===");
        output.WriteLine(console.Output);

        Assert.Contains("409", console.Output);
        Assert.Contains("Conflict", console.Output);
        Assert.Contains("Cluster 'prod' already exists.", console.Output);
    }

    [Fact]
    public void WriteHttpError_500_ShowsInternalError()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 500, "");

        output.WriteLine("=== HTTP 500 ===");
        output.WriteLine(console.Output);

        Assert.Contains("500", console.Output);
        Assert.Contains("Internal Server Error", console.Output);
    }

    [Fact]
    public void WriteHttpError_401_ShowsLoginHint()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 401, "");

        output.WriteLine("=== 401 Hint ===");
        output.WriteLine(console.Output);

        Assert.Contains("clustral login", console.Output);
    }

    [Fact]
    public void WriteHttpError_403_ShowsAccessRequestHint()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 403, "");

        output.WriteLine("=== 403 Hint ===");
        output.WriteLine(console.Output);

        Assert.Contains("clustral access request", console.Output);
    }

    [Fact]
    public void WriteHttpError_ShowsTraceId()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 500,
            """{"detail":"Something broke.","traceId":"00-abc123def456"}""");

        output.WriteLine("=== Trace ID ===");
        output.WriteLine(console.Output);

        Assert.Contains("00-abc123def456", console.Output);
    }

    [Fact]
    public void WriteHttpError_ShowsField()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 400,
            """{"detail":"Invalid format.","code":"INVALID_DURATION","field":"requestedDuration"}""");

        output.WriteLine("=== Field ===");
        output.WriteLine(console.Output);

        Assert.Contains("requestedDuration", console.Output);
        Assert.Contains("INVALID_DURATION", console.Output);
    }

    [Fact]
    public void WriteHttpError_FallsBackToLegacyErrorField()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 409, """{"error":"Already exists."}""");

        output.WriteLine("=== Legacy error field ===");
        output.WriteLine(console.Output);

        Assert.Contains("Already exists.", console.Output);
    }

    [Fact]
    public void WriteHttpError_FallsBackToPlainText()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 502, "Bad Gateway: upstream down");

        output.WriteLine("=== Plain text ===");
        output.WriteLine(console.Output);

        Assert.Contains("Bad Gateway: upstream down", console.Output);
    }

    [Fact]
    public void WriteHttpError_EmptyBody_UsesDefault()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteHttpError(console, 503, "");

        output.WriteLine("=== Empty body ===");
        output.WriteLine(console.Output);

        Assert.Contains("Service temporarily unavailable", console.Output);
    }

    // ── WriteConnectionError ────────────────────────────────────────────────

    [Fact]
    public void WriteConnectionError_SSL()
    {
        var ex = new HttpRequestException("The SSL connection could not be established.");
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteConnectionError(console, ex);

        output.WriteLine("=== SSL Error ===");
        output.WriteLine(console.Output);

        Assert.Contains("SSL/TLS failure", console.Output);
        Assert.Contains("--insecure", console.Output);
        Assert.Contains("Connection Error", console.Output);
    }

    [Fact]
    public void WriteConnectionError_SocketException()
    {
        var inner = new SocketException((int)SocketError.ConnectionRefused);
        var ex = new HttpRequestException("Connection refused", inner);
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteConnectionError(console, ex);

        output.WriteLine("=== Connection Refused ===");
        output.WriteLine(console.Output);

        Assert.Contains("Connection refused", console.Output);
        Assert.Contains("Is it running?", console.Output);
    }

    [Fact]
    public void WriteConnectionError_Timeout()
    {
        var ex = new TaskCanceledException("Timed out.");
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteConnectionError(console, ex);

        output.WriteLine("=== Timeout ===");
        output.WriteLine(console.Output);

        Assert.Contains("timeout", console.Output);
    }

    [Fact]
    public void WriteConnectionError_Cancelled()
    {
        var ex = new OperationCanceledException();
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteConnectionError(console, ex);

        output.WriteLine("=== Cancelled ===");
        output.WriteLine(console.Output);

        Assert.Contains("Cancelled", console.Output);
    }

    [Fact]
    public void WriteConnectionError_ShowsExceptionType()
    {
        var ex = new InvalidOperationException("Something unexpected.");
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteConnectionError(console, ex);

        output.WriteLine("=== Generic ===");
        output.WriteLine(console.Output);

        Assert.Contains("InvalidOperationException", console.Output);
        Assert.Contains("Something unexpected.", console.Output);
    }

    // ── WriteError ──────────────────────────────────────────────────────────

    [Fact]
    public void WriteError_ShowsMessage()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteError(console, "Role 'admin' not found.");

        output.WriteLine("=== Simple Error ===");
        output.WriteLine(console.Output);

        Assert.Contains("Error", console.Output);
        Assert.Contains("Role 'admin' not found.", console.Output);
    }

    [Fact]
    public void WriteError_EscapesMarkup()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteError(console, "Value contains [brackets] and {braces}.");

        output.WriteLine("=== Escaped ===");
        output.WriteLine(console.Output);

        Assert.Contains("brackets", console.Output);
    }

    // ── WriteNotConfigured ──────────────────────────────────────────────────

    [Fact]
    public void WriteNotConfigured_ShowsIssueAndFix()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteNotConfigured(console, "Not logged in", "clustral login");

        output.WriteLine("=== Not Configured ===");
        output.WriteLine(console.Output);

        Assert.Contains("Config", console.Output);
        Assert.Contains("Not logged in", console.Output);
        Assert.Contains("clustral login", console.Output);
    }

    [Fact]
    public void WriteNotConfigured_ControlPlaneUrl()
    {
        var console = new TestConsole(); console.Profile.Width = 80;
        CliErrors.WriteNotConfigured(console, "ControlPlane URL not configured", "clustral login <url>");

        output.WriteLine("=== ControlPlane URL ===");
        output.WriteLine(console.Output);

        Assert.Contains("ControlPlane URL", console.Output);
        Assert.Contains("clustral login", console.Output);
    }

    // ── Card structure ──────────────────────────────────────────────────────

    [Fact]
    public void AllCards_HaveRoundedBorders()
    {
        var c1 = new TestConsole(); c1.Profile.Width = 80;
        var c2 = new TestConsole(); c2.Profile.Width = 80;
        var c3 = new TestConsole(); c3.Profile.Width = 80;
        var c4 = new TestConsole(); c4.Profile.Width = 80;

        CliErrors.WriteHttpError(c1, 404, "");
        CliErrors.WriteConnectionError(c2, new Exception("test"));
        CliErrors.WriteError(c3, "test");
        CliErrors.WriteNotConfigured(c4, "test", "fix");

        output.WriteLine("=== All Card Types ===");
        output.WriteLine($"HTTP:   has borders = {c1.Output.Contains("─")}");
        output.WriteLine($"Conn:   has borders = {c2.Output.Contains("─")}");
        output.WriteLine($"Error:  has borders = {c3.Output.Contains("─")}");
        output.WriteLine($"Config: has borders = {c4.Output.Contains("─")}");

        Assert.All(new[] { c1.Output, c2.Output, c3.Output, c4.Output },
            card => Assert.Contains("─", card));
    }
}
