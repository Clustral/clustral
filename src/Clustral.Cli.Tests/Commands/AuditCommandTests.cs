using Clustral.Cli.Commands;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

public sealed class AuditCommandTests(ITestOutputHelper output)
{
    private static string Render(List<AuditEventItem> events)
    {
        var console = new TestConsole();
        console.Profile.Width = 120;
        AuditCommand.RenderAuditTable(console, events);
        return console.Output;
    }

    [Fact]
    public void RenderTable_ShowsAllColumns()
    {
        var events = new List<AuditEventItem>
        {
            new()
            {
                Code = "CAR002I",
                Event = "access_request.approved",
                Severity = "Info",
                User = "admin@corp.com",
                ClusterName = "prod",
                Time = DateTimeOffset.UtcNow.AddMinutes(-2),
                Message = "Access request abc123 approved",
            },
        };

        var rendered = Render(events);
        output.WriteLine(rendered);

        rendered.Should().Contain("CAR002I");
        rendered.Should().Contain("access_request.approved");
        rendered.Should().Contain("admin@corp.com");
        rendered.Should().Contain("prod");
        rendered.Should().Contain("2m ago");
        rendered.Should().Contain("Access request abc123 approved");
    }

    [Fact]
    public void RenderTable_WarningSeverity_ShowsYellow()
    {
        var events = new List<AuditEventItem>
        {
            new()
            {
                Code = "CAR003W",
                Event = "access_request.denied",
                Severity = "Warning",
                User = "reviewer@corp.com",
                Time = DateTimeOffset.UtcNow.AddMinutes(-5),
                Message = "Access request denied: not authorized",
            },
        };

        var rendered = Render(events);
        output.WriteLine(rendered);

        rendered.Should().Contain("CAR003W");
        rendered.Should().Contain("denied");
    }

    [Fact]
    public void RenderTable_NullFields_ShowsDash()
    {
        var events = new List<AuditEventItem>
        {
            new()
            {
                Code = "CUA001I",
                Event = "user.synced",
                Severity = "Info",
                User = null,
                ClusterName = null,
                Time = DateTimeOffset.UtcNow,
                Message = null,
            },
        };

        var rendered = Render(events);
        output.WriteLine(rendered);

        rendered.Should().Contain("CUA001I");
    }

    [Fact]
    public void RenderTable_LongMessage_Truncated()
    {
        var longMessage = new string('A', 100);
        var events = new List<AuditEventItem>
        {
            new()
            {
                Code = "CPR001I",
                Event = "proxy.request",
                Severity = "Info",
                Time = DateTimeOffset.UtcNow,
                Message = longMessage,
            },
        };

        var rendered = Render(events);
        output.WriteLine(rendered);

        rendered.Should().NotContain(longMessage, "message should be truncated");
    }

    [Fact]
    public void RenderTable_MultipleEvents_AllRendered()
    {
        var events = new List<AuditEventItem>
        {
            new() { Code = "CAR001I", Event = "access_request.created", Severity = "Info", Time = DateTimeOffset.UtcNow },
            new() { Code = "CAR002I", Event = "access_request.approved", Severity = "Info", Time = DateTimeOffset.UtcNow },
            new() { Code = "CCR001I", Event = "credential.issued", Severity = "Info", Time = DateTimeOffset.UtcNow },
        };

        var rendered = Render(events);
        output.WriteLine(rendered);

        rendered.Should().Contain("CAR001I");
        rendered.Should().Contain("CAR002I");
        rendered.Should().Contain("CCR001I");
    }
}
