using Clustral.Cli.Commands;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Tests for <see cref="DoctorCommand"/> rendering. Uses pre-built
/// <see cref="DoctorOutput"/> DTOs to test the Render method without
/// any network calls.
/// </summary>
public sealed class DoctorCommandTests(ITestOutputHelper output)
{
    private static string Render(DoctorOutput data)
    {
        var console = new TestConsole();
        console.Profile.Width = 100;
        DoctorCommand.Render(console, data);
        return console.Output;
    }

    // ── Individual check rendering ──────────────────────────────────────────

    [Fact]
    public void Render_PassedCheck_ShowsGreenCheckmark()
    {
        var data = new DoctorOutput
        {
            Checks = [new DoctorCheck { Name = "DNS resolution", Status = "pass", Detail = "localhost → 127.0.0.1", ElapsedMs = 2 }],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("✓");
        rendered.Should().Contain("DNS resolution");
        rendered.Should().Contain("127.0.0.1");
        rendered.Should().Contain("2ms");
    }

    [Fact]
    public void Render_FailedCheck_ShowsRedCross()
    {
        var data = new DoctorOutput
        {
            Checks = [new DoctorCheck { Name = "ControlPlane health", Status = "fail", Detail = "Connection refused", ElapsedMs = 50 }],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("✗");
        rendered.Should().Contain("ControlPlane health");
        rendered.Should().Contain("Connection refused");
    }

    [Fact]
    public void Render_WarningCheck_ShowsYellowExclamation()
    {
        var data = new DoctorOutput
        {
            Checks = [new DoctorCheck { Name = "JWT session", Status = "warn", Detail = "Expired. Run: clustral login" }],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("!");
        rendered.Should().Contain("JWT session");
        rendered.Should().Contain("Expired");
    }

    [Fact]
    public void Render_SkippedCheck_ShowsDash()
    {
        var data = new DoctorOutput
        {
            Checks = [new DoctorCheck { Name = "TLS handshake", Status = "skip", Detail = "Skipped (--insecure)" }],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("–");
        rendered.Should().Contain("TLS handshake");
        rendered.Should().Contain("Skipped");
    }

    // ── Summary line ────────────────────────────────────────────────────────

    [Fact]
    public void Render_AllPassed_ShowsAllChecksPassed()
    {
        var data = new DoctorOutput
        {
            Checks =
            [
                new DoctorCheck { Name = "Config", Status = "pass", Detail = "OK" },
                new DoctorCheck { Name = "DNS", Status = "pass", Detail = "OK" },
                new DoctorCheck { Name = "Health", Status = "pass", Detail = "OK" },
            ],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("All checks passed");
    }

    [Fact]
    public void Render_WithWarnings_ShowsWarningCount()
    {
        var data = new DoctorOutput
        {
            Checks =
            [
                new DoctorCheck { Name = "Config", Status = "pass", Detail = "OK" },
                new DoctorCheck { Name = "JWT", Status = "warn", Detail = "Expired" },
            ],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("1 warning");
        rendered.Should().Contain("1 passed");
    }

    [Fact]
    public void Render_WithFailures_ShowsFailedCount()
    {
        var data = new DoctorOutput
        {
            Checks =
            [
                new DoctorCheck { Name = "Config", Status = "pass", Detail = "OK" },
                new DoctorCheck { Name = "DNS", Status = "fail", Detail = "Could not resolve" },
                new DoctorCheck { Name = "JWT", Status = "warn", Detail = "Expired" },
            ],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("1 failed");
        rendered.Should().Contain("1 warning");
        rendered.Should().Contain("1 passed");
    }

    // ── Full doctor output ──────────────────────────────────────────────────

    [Fact]
    public void Render_FullDoctorOutput_AllSectionsPresent()
    {
        var data = new DoctorOutput
        {
            Checks =
            [
                new DoctorCheck { Name = "Configuration", Status = "pass", Detail = "ControlPlane URL: https://cp.example.com" },
                new DoctorCheck { Name = "DNS resolution", Status = "pass", Detail = "cp.example.com → 1.2.3.4", ElapsedMs = 3 },
                new DoctorCheck { Name = "TLS handshake", Status = "pass", Detail = "Valid certificate, status 200", ElapsedMs = 45 },
                new DoctorCheck { Name = "ControlPlane health", Status = "pass", Detail = "v1.2.0, 200 OK", ElapsedMs = 38 },
                new DoctorCheck { Name = "OIDC discovery", Status = "pass", Detail = "http://keycloak:8080 (200 OK)", ElapsedMs = 22 },
                new DoctorCheck { Name = "JWT session", Status = "pass", Detail = "Valid, expires 2026-04-11 22:00 [11h remaining]" },
                new DoctorCheck { Name = "Kubeconfig", Status = "pass", Detail = "2 clustral contexts, 2 with active credentials" },
            ],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("Configuration");
        rendered.Should().Contain("DNS resolution");
        rendered.Should().Contain("TLS handshake");
        rendered.Should().Contain("ControlPlane health");
        rendered.Should().Contain("OIDC discovery");
        rendered.Should().Contain("JWT session");
        rendered.Should().Contain("Kubeconfig");
        rendered.Should().Contain("All checks passed");
    }

    [Fact]
    public void Render_EarlyFailure_OnlyShowsChecksUpToFailure()
    {
        // Simulates what RunChecksAsync returns when DNS fails —
        // only Config + DNS checks, no subsequent ones.
        var data = new DoctorOutput
        {
            Checks =
            [
                new DoctorCheck { Name = "Configuration", Status = "pass", Detail = "OK" },
                new DoctorCheck { Name = "DNS resolution", Status = "fail", Detail = "Could not resolve hostname", ElapsedMs = 100 },
            ],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("Configuration");
        rendered.Should().Contain("DNS resolution");
        rendered.Should().Contain("1 failed");
        rendered.Should().NotContain("ControlPlane health");
    }

    // ── No timing ───────────────────────────────────────────────────────────

    [Fact]
    public void Render_NoElapsedMs_OmitsTiming()
    {
        var data = new DoctorOutput
        {
            Checks = [new DoctorCheck { Name = "JWT session", Status = "pass", Detail = "Valid" }],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().NotContain("ms)");
    }
}
