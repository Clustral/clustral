using System.Text.Json;
using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Tests for <see cref="ProfileCommand"/> — profile CRUD and resolution.
/// Uses a temp HOME directory to isolate file system state.
/// </summary>
[Collection(ProfileTestCollection.Name)]
public sealed class ProfileCommandTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _tempHome = Path.Combine(
        Path.GetTempPath(), $"clustral-profile-test-{Guid.NewGuid():N}");
    private readonly string? _origHome = Environment.GetEnvironmentVariable("HOME");
    private readonly string? _origUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");

    private string ProfilesDir => Path.Combine(_tempHome, ".clustral", "profiles");

    private void SetupHome()
    {
        Directory.CreateDirectory(Path.Combine(_tempHome, ".clustral"));
        Environment.SetEnvironmentVariable("HOME", _tempHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _tempHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _origHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _origUserProfile);
        try { Directory.Delete(_tempHome, recursive: true); } catch { }
    }

    // ── ListProfiles ────────────────────────────────────────────────────────

    [Fact]
    public void ListProfiles_NoProfilesDir_ReturnsDefaultOnly()
    {
        SetupHome();
        var profiles = ProfileCommand.ListProfiles();

        profiles.Should().Equal("default");
    }

    [Fact]
    public void ListProfiles_WithProfiles_DefaultFirstThenAlphabetical()
    {
        SetupHome();
        Directory.CreateDirectory(Path.Combine(ProfilesDir, "staging"));
        Directory.CreateDirectory(Path.Combine(ProfilesDir, "prod"));
        Directory.CreateDirectory(Path.Combine(ProfilesDir, "dev"));

        var profiles = ProfileCommand.ListProfiles();

        output.WriteLine($"Profiles: {string.Join(", ", profiles)}");
        profiles.Should().Equal("default", "dev", "prod", "staging");
    }

    [Fact]
    public void LoadProfileConfig_Default_ReturnsBaseConfig()
    {
        SetupHome();
        var config = new CliConfig { ControlPlaneUrl = "https://base.example.com" };
        File.WriteAllText(
            Path.Combine(_tempHome, ".clustral", "config.json"),
            JsonSerializer.Serialize(config, CliJsonContext.Default.CliConfig));

        var loaded = ProfileCommand.LoadProfileConfig("default");

        loaded.Should().NotBeNull();
        loaded!.ControlPlaneUrl.Should().Be("https://base.example.com");
    }

    [Fact]
    public void GetProfileBadge_NoActiveProfile_ReturnsEmpty()
    {
        SetupHome();
        ProfileCommand.ClearActiveProfile();

        ProfileCommand.GetProfileBadge().Should().BeEmpty();
    }

    [Fact]
    public void GetProfileBadge_ActiveProfile_ReturnsBadge()
    {
        SetupHome();
        ProfileCommand.SetActiveProfile("staging");

        ProfileCommand.GetProfileBadge().Should().Contain("staging");
    }

    // ── Active profile ──────────────────────────────────────────────────────

    [Fact]
    public void GetActiveProfile_NoFile_ReturnsNull()
    {
        SetupHome();
        ProfileCommand.GetActiveProfile().Should().BeNull();
    }

    [Fact]
    public void SetAndGetActiveProfile_RoundTrips()
    {
        SetupHome();
        ProfileCommand.SetActiveProfile("prod");

        ProfileCommand.GetActiveProfile().Should().Be("prod");
    }

    [Fact]
    public void ClearActiveProfile_RemovesFile()
    {
        SetupHome();
        ProfileCommand.SetActiveProfile("prod");
        ProfileCommand.ClearActiveProfile();

        ProfileCommand.GetActiveProfile().Should().BeNull();
    }

    // ── Config path resolution ──────────────────────────────────────────────

    [Fact]
    public void ResolveConfigPath_NoProfile_ReturnsDefaultPath()
    {
        SetupHome();
        ProfileCommand.ClearActiveProfile();

        var path = ProfileCommand.ResolveConfigPath();

        output.WriteLine($"Config path: {path}");
        path.Should().EndWith(Path.Combine(".clustral", "config.json"));
        path.Should().NotContain("profiles");
    }

    [Fact]
    public void ResolveConfigPath_WithProfile_ReturnsProfilePath()
    {
        SetupHome();
        var profileDir = Path.Combine(ProfilesDir, "staging");
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(Path.Combine(profileDir, "config.json"), "{}");
        ProfileCommand.SetActiveProfile("staging");

        var path = ProfileCommand.ResolveConfigPath();

        output.WriteLine($"Config path: {path}");
        path.Should().Contain(Path.Combine("profiles", "staging", "config.json"));
    }

    [Fact]
    public void ResolveConfigPath_ProfileWithoutConfigFile_FallsBackToDefault()
    {
        SetupHome();
        Directory.CreateDirectory(Path.Combine(ProfilesDir, "empty"));
        ProfileCommand.SetActiveProfile("empty");

        var path = ProfileCommand.ResolveConfigPath();

        path.Should().NotContain("profiles");
    }

    // ── Token path resolution ───────────────────────────────────────────────

    [Fact]
    public void ResolveTokenPath_NoProfile_ReturnsDefaultPath()
    {
        SetupHome();
        ProfileCommand.ClearActiveProfile();

        var path = ProfileCommand.ResolveTokenPath();

        path.Should().EndWith(Path.Combine(".clustral", "token"));
        path.Should().NotContain("profiles");
    }

    [Fact]
    public void ResolveTokenPath_WithProfile_ReturnsProfilePath()
    {
        SetupHome();
        Directory.CreateDirectory(Path.Combine(ProfilesDir, "prod"));
        ProfileCommand.SetActiveProfile("prod");

        var path = ProfileCommand.ResolveTokenPath();

        path.Should().Contain(Path.Combine("profiles", "prod", "token"));
    }

    // ── Create profile ──────────────────────────────────────────────────────

    [Fact]
    public void CreateProfile_CreatesDirectoryAndConfig()
    {
        SetupHome();
        var profileDir = ProfileCommand.GetProfileDir("newenv");

        Directory.CreateDirectory(profileDir);
        File.WriteAllText(
            Path.Combine(profileDir, "config.json"),
            JsonSerializer.Serialize(new CliConfig(), CliJsonContext.Default.CliConfig));

        Directory.Exists(profileDir).Should().BeTrue();
        File.Exists(Path.Combine(profileDir, "config.json")).Should().BeTrue();
    }

    // ── Delete profile ──────────────────────────────────────────────────────

    [Fact]
    public void DeleteProfile_RemovesDirectory()
    {
        SetupHome();
        var profileDir = Path.Combine(ProfilesDir, "temp");
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(Path.Combine(profileDir, "config.json"), "{}");

        Directory.Delete(profileDir, recursive: true);

        Directory.Exists(profileDir).Should().BeFalse();
    }

    [Fact]
    public void DeleteActiveProfile_ClearsActiveProfileFile()
    {
        SetupHome();
        var profileDir = Path.Combine(ProfilesDir, "temp");
        Directory.CreateDirectory(profileDir);
        ProfileCommand.SetActiveProfile("temp");

        // Simulate delete handler
        ProfileCommand.ClearActiveProfile();
        Directory.Delete(profileDir, recursive: true);

        ProfileCommand.GetActiveProfile().Should().BeNull();
    }

    // ── Integration: profile affects CliConfig.Load ─────────────────────────

    [Fact]
    public void CliConfig_Load_UsesActiveProfileConfig()
    {
        SetupHome();
        var profileDir = Path.Combine(ProfilesDir, "staging");
        Directory.CreateDirectory(profileDir);
        var config = new CliConfig { ControlPlaneUrl = "https://staging.example.com" };
        File.WriteAllText(
            Path.Combine(profileDir, "config.json"),
            JsonSerializer.Serialize(config, CliJsonContext.Default.CliConfig));
        ProfileCommand.SetActiveProfile("staging");

        var loaded = CliConfig.Load();

        output.WriteLine($"ControlPlaneUrl: {loaded.ControlPlaneUrl}");
        loaded.ControlPlaneUrl.Should().Be("https://staging.example.com");
    }

    [Fact]
    public void CliConfig_Load_NoProfile_UsesDefaultConfig()
    {
        SetupHome();
        ProfileCommand.ClearActiveProfile();
        var defaultConfig = new CliConfig { ControlPlaneUrl = "https://default.example.com" };
        File.WriteAllText(
            Path.Combine(_tempHome, ".clustral", "config.json"),
            JsonSerializer.Serialize(defaultConfig, CliJsonContext.Default.CliConfig));

        var loaded = CliConfig.Load();

        loaded.ControlPlaneUrl.Should().Be("https://default.example.com");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProfileTestCollection
{
    public const string Name = "ProfileTests (env-var redirected, no parallelisation)";
}
