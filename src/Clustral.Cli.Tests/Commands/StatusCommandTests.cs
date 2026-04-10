using Clustral.Cli.Commands;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Tests for <see cref="StatusCommand"/> rendering. Uses pre-built
/// <see cref="StatusOutput"/> DTOs to test the Render method without
/// any HTTP calls or file system access.
/// </summary>
public sealed class StatusCommandTests(ITestOutputHelper output)
{
    private static string Render(StatusOutput data)
    {
        var console = new TestConsole();
        console.Profile.Width = 100;
        StatusCommand.Render(console, data);
        return console.Output;
    }

    // ── Session ─────────────────────────────────────────────────────────────

    [Fact]
    public void Render_NotLoggedIn_ShowsNotLoggedIn()
    {
        var data = new StatusOutput();
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("Session");
        rendered.Should().Contain("Not logged in");
    }

    [Fact]
    public void Render_LoggedIn_ShowsEmailAndExpiry()
    {
        var data = new StatusOutput
        {
            Session = new StatusSession
            {
                LoggedIn = true,
                Email = "alice@example.com",
                Valid = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
            },
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("alice@example.com");
        rendered.Should().Contain("Valid until");
        rendered.Should().Contain("valid for");
    }

    [Fact]
    public void Render_ExpiredSession_ShowsExpired()
    {
        var data = new StatusOutput
        {
            Session = new StatusSession
            {
                LoggedIn = true,
                Email = "alice@example.com",
                Valid = false,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            },
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("alice@example.com");
        rendered.Should().Contain("expired");
    }

    // ── Clusters ────────────────────────────────────────────────────────────

    [Fact]
    public void Render_NoClusters_ShowsEmptyMessage()
    {
        var data = new StatusOutput();
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("No clustral contexts");
    }

    [Fact]
    public void Render_WithClusters_ShowsContextNames()
    {
        var data = new StatusOutput
        {
            Clusters =
            [
                new StatusCluster { ContextName = "clustral-prod", HasToken = true },
                new StatusCluster { ContextName = "clustral-staging", HasToken = false },
            ],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("clustral-prod");
        rendered.Should().Contain("clustral-staging");
    }

    // ── Grants ──────────────────────────────────────────────────────────────

    [Fact]
    public void Render_WithGrants_ShowsJitRemaining()
    {
        var data = new StatusOutput
        {
            Grants =
            [
                new StatusGrant
                {
                    ClusterName = "prod",
                    RoleName = "read-only",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(3),
                },
            ],
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("Active Grants");
        rendered.Should().Contain("prod");
        rendered.Should().Contain("read-only");
        rendered.Should().Contain("JIT");
        rendered.Should().Contain("remaining");
    }

    [Fact]
    public void Render_NoGrants_OmitsSection()
    {
        var data = new StatusOutput();
        var rendered = Render(data);

        rendered.Should().NotContain("Active Grants");
    }

    // ── ControlPlane ────────────────────────────────────────────────────────

    [Fact]
    public void Render_ControlPlaneOnline_ShowsGreenIndicator()
    {
        var data = new StatusOutput
        {
            ControlPlane = new StatusControlPlane
            {
                Url = "https://cp.example.com",
                Online = true,
                Version = "1.2.0",
            },
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("ControlPlane");
        rendered.Should().Contain("Online");
        rendered.Should().Contain("cp.example.com");
        rendered.Should().Contain("1.2.0");
    }

    [Fact]
    public void Render_ControlPlaneOffline_ShowsRedIndicator()
    {
        var data = new StatusOutput
        {
            ControlPlane = new StatusControlPlane
            {
                Url = "https://cp.example.com",
                Online = false,
            },
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("Unreachable");
        rendered.Should().Contain("cp.example.com");
    }

    [Fact]
    public void Render_NoControlPlane_OmitsSection()
    {
        var data = new StatusOutput();
        var rendered = Render(data);

        rendered.Should().NotContain("ControlPlane");
    }

    // ── Full status ─────────────────────────────────────────────────────────

    [Fact]
    public void Render_FullStatus_AllSectionsPresent()
    {
        var data = new StatusOutput
        {
            Session = new StatusSession
            {
                LoggedIn = true,
                Email = "admin@clustral.io",
                Valid = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(12),
            },
            Clusters =
            [
                new StatusCluster { ContextName = "clustral-prod", HasToken = true },
            ],
            Grants =
            [
                new StatusGrant
                {
                    ClusterName = "prod",
                    RoleName = "admin",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
                },
            ],
            ControlPlane = new StatusControlPlane
            {
                Url = "https://cp.clustral.io",
                Online = true,
                Version = "2.0.0",
            },
        };
        var rendered = Render(data);

        output.WriteLine(rendered);

        rendered.Should().Contain("Session");
        rendered.Should().Contain("admin@clustral.io");
        rendered.Should().Contain("Clusters");
        rendered.Should().Contain("clustral-prod");
        rendered.Should().Contain("Active Grants");
        rendered.Should().Contain("ControlPlane");
        rendered.Should().Contain("Online");
    }
}
