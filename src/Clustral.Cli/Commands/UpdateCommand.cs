using System.CommandLine;
using System.CommandLine.Invocation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Clustral.Cli.Ui;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral update</c>: checks GitHub Releases for a newer
/// version and replaces the current binary.
/// </summary>
internal static class UpdateCommand
{
    private const string Repo = "Clustral/clustral";

    private static readonly Option<bool> PreOption = new(
        "--pre",
        "Include pre-release versions.");

    private static readonly Option<bool> CheckOption = new(
        "--check",
        "Only check for updates, don't install.");

    public static Command Build()
    {
        var cmd = new Command("update", "Update clustral to the latest version.");
        cmd.AddOption(PreOption);
        cmd.AddOption(CheckOption);
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct       = ctx.GetCancellationToken();
        var pre      = ctx.ParseResult.GetValueForOption(PreOption);
        var checkOnly = ctx.ParseResult.GetValueForOption(CheckOption);

        var currentVersion = VersionCommand.GetVersion();
        CliDebug.Log($"Current version: v{currentVersion}");

        Console.WriteLine($"Current version: v{currentVersion}");

        // GitHub gets a longer timeout because the binary download is the slow path.
        var innerHandler = new HttpClientHandler();
        HttpMessageHandler pipeline = CliDebug.Enabled
            ? new Http.DebugLoggingHandler(innerHandler)
            : innerHandler;
        using var http = new HttpClient(pipeline)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("clustral-cli");

        // Fetch release info.
        string apiUrl;
        if (pre)
        {
            apiUrl = $"https://api.github.com/repos/{Repo}/releases";
        }
        else
        {
            apiUrl = $"https://api.github.com/repos/{Repo}/releases/latest";
        }

        var json = await Http.CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.CheckingUpdates,
            innerCt => http.GetStringAsync(apiUrl, innerCt),
            ct);

        string? tagName = null;
        string? assetUrl = null;
        var artifactName = GetArtifactName();

        if (pre)
        {
            // Parse array of releases, find first with our artifact.
            using var doc = JsonDocument.Parse(json);
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                tagName = release.GetProperty("tag_name").GetString();
                assetUrl = FindAssetUrl(release, artifactName);
                if (assetUrl is not null) break;
            }
        }
        else
        {
            using var doc = JsonDocument.Parse(json);
            tagName = doc.RootElement.GetProperty("tag_name").GetString();
            assetUrl = FindAssetUrl(doc.RootElement, artifactName);
        }

        if (tagName is null)
        {
            Console.WriteLine("No releases found.");
            return;
        }

        var latestVersion = tagName.TrimStart('v');
        CliDebug.Log($"Artifact: {artifactName}, latest: v{latestVersion}, asset: {assetUrl ?? "(not found)"}");
        Console.WriteLine($"Latest version:  v{latestVersion}");

        if (latestVersion == currentVersion)
        {
            Console.WriteLine("Already up to date.");
            return;
        }

        if (checkOnly)
        {
            Console.WriteLine($"Update available: v{currentVersion} → v{latestVersion}");
            return;
        }

        if (assetUrl is null)
        {
            CliErrors.WriteError(Messages.Errors.NoBinary(artifactName, tagName));
            ctx.ExitCode = 1;
            return;
        }

        var binaryData = await Http.CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.Downloading(latestVersion, artifactName),
            innerCt => http.GetByteArrayAsync(assetUrl, innerCt),
            ct);

        // Replace the current binary.
        var currentPath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(currentPath))
        {
            CliErrors.WriteError(Messages.Errors.CannotDetermineBinaryPath);
            ctx.ExitCode = 1;
            return;
        }

        var tempPath = currentPath + ".new";
        await File.WriteAllBytesAsync(tempPath, binaryData, ct);

        // On Unix, set executable permission.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(tempPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        // Atomic replace: rename old → .old, rename new → current.
        var oldPath = currentPath + ".old";
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(currentPath, oldPath);
        File.Move(tempPath, currentPath);
        File.Delete(oldPath);

        CliDebug.Log($"Replaced binary at {currentPath}");
        Console.WriteLine($"Updated to v{latestVersion}.");
    }

    internal static string GetArtifactName()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
               : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? "darwin"
               : "linux";

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _                  => "amd64",
        };

        var ext = os == "windows" ? ".exe" : "";
        return $"clustral-{os}-{arch}{ext}";
    }

    internal static string? FindAssetUrl(JsonElement release, string artifactName)
    {
        if (!release.TryGetProperty("assets", out var assets)) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name == artifactName)
                return asset.GetProperty("browser_download_url").GetString();
        }
        return null;
    }
}
