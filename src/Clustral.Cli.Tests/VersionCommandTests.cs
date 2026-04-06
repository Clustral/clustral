using Clustral.Cli.Commands;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class VersionCommandTests(ITestOutputHelper output)
{
    [Fact]
    public void GetVersion_ReturnsNonEmptyString()
    {
        var version = VersionCommand.GetVersion();

        output.WriteLine($"Version: {version}");

        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    [Fact]
    public void GetVersion_DoesNotStartWithV()
    {
        var version = VersionCommand.GetVersion();

        output.WriteLine($"Raw version output: \"{version}\"");
        output.WriteLine($"Formatted:          v{version}");

        Assert.False(version.StartsWith('v'), "GetVersion() should not include the 'v' prefix.");
    }
}
