using System.Net.Sockets;
using Clustral.Cli.Http;
using Clustral.Cli.Ui;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

/// <summary>
/// Tests for <see cref="CliExceptionHandler"/> — the global exception
/// classifier that mirrors the ControlPlane's
/// <c>GlobalExceptionHandlerMiddleware.ClassifyException</c>.
/// </summary>
public sealed class CliExceptionHandlerTests(ITestOutputHelper output) : IDisposable
{
    // Save/restore CliDebug.Enabled so tests don't leak state.
    private readonly bool _origDebug = CliDebug.Enabled;

    public void Dispose() => CliDebug.Enabled = _origDebug;

    // ─────────────────────────────────────────────────────────────────────────
    // Classification — each exception type maps to the right exit code + output
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CliHttpTimeoutException_ExitCode1_ShowsTimeoutError()
    {
        CliDebug.Enabled = false;
        var console = new TestConsole(); console.Profile.Width = 120;
        var ex = new CliHttpTimeoutException("Loading clusters...");

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine(console.Output);
        exitCode.Should().Be(1);
        console.Output.Should().Contain("timed out");
    }

    [Fact]
    public void CliHttpErrorException_ExitCode1_ShowsHttpError()
    {
        CliDebug.Enabled = false;
        var console = new TestConsole(); console.Profile.Width = 120;
        var ex = new CliHttpErrorException(404, """{"detail":"Cluster not found.","code":"CLUSTER_NOT_FOUND"}""");

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine(console.Output);
        exitCode.Should().Be(1);
        console.Output.Should().Contain("404");
        console.Output.Should().Contain("Cluster not found.");
    }

    [Fact]
    public void HttpRequestException_SocketException_ExitCode1_ShowsConnectionError()
    {
        CliDebug.Enabled = false;
        var console = new TestConsole(); console.Profile.Width = 120;
        var inner = new SocketException((int)SocketError.ConnectionRefused);
        var ex = new HttpRequestException("Connection refused", inner);

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine(console.Output);
        exitCode.Should().Be(1);
        console.Output.Should().Contain("Connection refused");
        console.Output.Should().Contain("Connection Error");
    }

    [Fact]
    public void HttpRequestException_SSL_ExitCode1_ShowsSSLError()
    {
        CliDebug.Enabled = false;
        var console = new TestConsole(); console.Profile.Width = 120;
        var ex = new HttpRequestException("The SSL connection could not be established.");

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine(console.Output);
        exitCode.Should().Be(1);
        console.Output.Should().Contain("SSL");
    }

    [Fact]
    public void TaskCanceledException_ExitCode1_ShowsTimeout()
    {
        CliDebug.Enabled = false;
        var console = new TestConsole(); console.Profile.Width = 120;
        var ex = new TaskCanceledException("Timed out");

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine(console.Output);
        exitCode.Should().Be(1);
        console.Output.Should().Contain("timeout");
    }

    [Fact]
    public void OperationCanceledException_ExitCode130_NoOutput()
    {
        CliDebug.Enabled = false;
        var console = new TestConsole(); console.Profile.Width = 120;
        var ex = new OperationCanceledException();

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine($"Output: '{console.Output}'");
        exitCode.Should().Be(130);
        console.Output.Trim().Should().BeEmpty("user cancellation should produce no error output");
    }

    [Fact]
    public void GenericException_ExitCode1_ShowsUnexpectedError()
    {
        CliDebug.Enabled = false;
        var console = new TestConsole(); console.Profile.Width = 120;
        var ex = new InvalidOperationException("Something broke.");

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine(console.Output);
        exitCode.Should().Be(1);
        console.Output.Should().Contain("Unexpected error");
        console.Output.Should().Contain("Something broke.");
        console.Output.Should().Contain("--debug");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Debug mode — full exception details shown
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DebugMode_GenericException_ShowsFullStackTrace()
    {
        CliDebug.Enabled = true;
        var console = new TestConsole(); console.Profile.Width = 120;
        var ex = new InvalidOperationException("Something broke.");

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine(console.Output);
        exitCode.Should().Be(1);
        console.Output.Should().Contain("Exception details:");
        console.Output.Should().Contain("InvalidOperationException");
        console.Output.Should().Contain("Something broke.");
        // Should NOT show the "re-run with --debug" hint in debug mode.
        console.Output.Should().NotContain("Re-run with --debug");
    }

    [Fact]
    public void DebugMode_HttpRequestException_ShowsFullStackTrace()
    {
        CliDebug.Enabled = true;
        var console = new TestConsole(); console.Profile.Width = 120;
        var inner = new SocketException((int)SocketError.ConnectionRefused);
        var ex = new HttpRequestException("Connection refused", inner);

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine(console.Output);
        exitCode.Should().Be(1);
        console.Output.Should().Contain("Exception details:");
        console.Output.Should().Contain("HttpRequestException");
        console.Output.Should().Contain("SocketException");
    }

    [Fact]
    public void DebugMode_CliHttpErrorException_ShowsFullDetails()
    {
        CliDebug.Enabled = true;
        var console = new TestConsole(); console.Profile.Width = 120;
        var ex = new CliHttpErrorException(500, "Internal Server Error");

        var exitCode = CliExceptionHandler.Handle(console, ex);

        output.WriteLine(console.Output);
        exitCode.Should().Be(1);
        console.Output.Should().Contain("500");
        console.Output.Should().Contain("Exception details:");
    }

    [Fact]
    public void DebugMode_OperationCancelled_StillNoOutput()
    {
        CliDebug.Enabled = true;
        var console = new TestConsole(); console.Profile.Width = 120;
        var ex = new OperationCanceledException();

        var exitCode = CliExceptionHandler.Handle(console, ex);

        exitCode.Should().Be(130);
        console.Output.Trim().Should().BeEmpty(
            "even in debug mode, user cancellation should be silent");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Classification correctness
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_ReturnsCorrectExitCodesForAllTypes()
    {
        CliExceptionHandler.Classify(new CliHttpTimeoutException("test")).ExitCode.Should().Be(1);
        CliExceptionHandler.Classify(new CliHttpErrorException(404, "")).ExitCode.Should().Be(1);
        CliExceptionHandler.Classify(new HttpRequestException()).ExitCode.Should().Be(1);
        CliExceptionHandler.Classify(new TaskCanceledException()).ExitCode.Should().Be(1);
        CliExceptionHandler.Classify(new OperationCanceledException()).ExitCode.Should().Be(130);
        CliExceptionHandler.Classify(new InvalidOperationException()).ExitCode.Should().Be(1);
        CliExceptionHandler.Classify(new Exception()).ExitCode.Should().Be(1);
    }
}
