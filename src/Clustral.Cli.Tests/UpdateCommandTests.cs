using System.Text.Json;
using Clustral.Cli.Commands;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class UpdateCommandTests(ITestOutputHelper output)
{
    [Fact]
    public void GetArtifactName_ReturnsValidFormat()
    {
        var name = UpdateCommand.GetArtifactName();

        output.WriteLine($"Platform artifact name: {name}");

        Assert.StartsWith("clustral-", name);
        Assert.Matches(@"^clustral-(darwin|linux|windows)-(arm64|amd64)(\.exe)?$", name);
    }

    [Fact]
    public void FindAssetUrl_FindsMatchingAsset()
    {
        var json = """
        {
            "tag_name": "v0.2.0",
            "assets": [
                { "name": "clustral-linux-amd64", "browser_download_url": "https://github.com/releases/clustral-linux-amd64" },
                { "name": "clustral-darwin-arm64", "browser_download_url": "https://github.com/releases/clustral-darwin-arm64" },
                { "name": "clustral-windows-amd64.exe", "browser_download_url": "https://github.com/releases/clustral-windows-amd64.exe" }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);

        foreach (var artifact in new[] { "clustral-linux-amd64", "clustral-darwin-arm64", "clustral-windows-amd64.exe" })
        {
            var url = UpdateCommand.FindAssetUrl(doc.RootElement, artifact);
            output.WriteLine($"FindAssetUrl(\"{artifact}\") => {url}");
            Assert.NotNull(url);
        }
    }

    [Fact]
    public void FindAssetUrl_ReturnsNull_WhenNoMatch()
    {
        var json = """
        {
            "tag_name": "v0.1.0",
            "assets": [
                { "name": "clustral-linux-amd64", "browser_download_url": "https://example.com/linux" }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = UpdateCommand.FindAssetUrl(doc.RootElement, "clustral-darwin-arm64");

        output.WriteLine($"FindAssetUrl(\"clustral-darwin-arm64\") => {(result is null ? "null" : result)}");
        output.WriteLine("  (only linux-amd64 available in release)");

        Assert.Null(result);
    }

    [Fact]
    public void FindAssetUrl_ReturnsNull_WhenNoAssetsKey()
    {
        var json = """{ "tag_name": "v0.1.0" }""";

        using var doc = JsonDocument.Parse(json);
        var result = UpdateCommand.FindAssetUrl(doc.RootElement, "clustral-linux-amd64");

        output.WriteLine($"Release JSON has no 'assets' key => {(result is null ? "null" : result)}");

        Assert.Null(result);
    }

    [Fact]
    public void FindAssetUrl_ReturnsNull_WhenAssetsEmpty()
    {
        var json = """{ "tag_name": "v0.1.0", "assets": [] }""";

        using var doc = JsonDocument.Parse(json);
        var result = UpdateCommand.FindAssetUrl(doc.RootElement, "clustral-linux-amd64");

        output.WriteLine($"Release has empty assets array => {(result is null ? "null" : result)}");

        Assert.Null(result);
    }
}
