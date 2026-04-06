using System.Text.Json;
using Clustral.Cli.Config;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class CliConfigTests(ITestOutputHelper output)
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new CliConfig();

        output.WriteLine("=== CliConfig Defaults ===");
        output.WriteLine($"  OidcAuthority:   \"{config.OidcAuthority}\"");
        output.WriteLine($"  OidcClientId:    \"{config.OidcClientId}\"");
        output.WriteLine($"  OidcScopes:      \"{config.OidcScopes}\"");
        output.WriteLine($"  ControlPlaneUrl: \"{config.ControlPlaneUrl}\"");
        output.WriteLine($"  CallbackPort:    {config.CallbackPort}");
        output.WriteLine($"  InsecureTls:     {config.InsecureTls}");

        Assert.Equal("clustral-cli", config.OidcClientId);
        Assert.Equal("openid email profile", config.OidcScopes);
        Assert.Equal(7777, config.CallbackPort);
        Assert.False(config.InsecureTls);
        Assert.Equal(string.Empty, config.OidcAuthority);
        Assert.Equal(string.Empty, config.ControlPlaneUrl);
    }

    [Fact]
    public void JsonRoundTrip_PreservesAllFields()
    {
        var original = new CliConfig
        {
            OidcAuthority = "http://keycloak:8080/realms/clustral",
            OidcClientId = "custom-client",
            OidcScopes = "openid email",
            ControlPlaneUrl = "https://cp.example.com",
            CallbackPort = 9999,
            InsecureTls = true,
        };

        var json = JsonSerializer.Serialize(original, CliJsonContext.Default.CliConfig);

        output.WriteLine("=== Serialized JSON ===");
        output.WriteLine(json);

        var restored = JsonSerializer.Deserialize(json, CliJsonContext.Default.CliConfig);

        Assert.NotNull(restored);
        Assert.Equal(original.OidcAuthority, restored!.OidcAuthority);
        Assert.Equal(original.OidcClientId, restored.OidcClientId);
        Assert.Equal(original.OidcScopes, restored.OidcScopes);
        Assert.Equal(original.ControlPlaneUrl, restored.ControlPlaneUrl);
        Assert.Equal(original.CallbackPort, restored.CallbackPort);
        Assert.Equal(original.InsecureTls, restored.InsecureTls);
    }

    [Fact]
    public void JsonDeserialization_UsesCamelCase()
    {
        var json = """
        {
            "oidcAuthority": "http://localhost:8080/realms/test",
            "oidcClientId": "test-client",
            "controlPlaneUrl": "http://localhost:5000",
            "callbackPort": 8888,
            "insecureTls": true
        }
        """;

        output.WriteLine("=== Input JSON ===");
        output.WriteLine(json);

        var config = JsonSerializer.Deserialize(json, CliJsonContext.Default.CliConfig);

        output.WriteLine("=== Parsed Config ===");
        output.WriteLine($"  OidcAuthority:   {config!.OidcAuthority}");
        output.WriteLine($"  OidcClientId:    {config.OidcClientId}");
        output.WriteLine($"  ControlPlaneUrl: {config.ControlPlaneUrl}");
        output.WriteLine($"  CallbackPort:    {config.CallbackPort}");
        output.WriteLine($"  InsecureTls:     {config.InsecureTls}");

        Assert.NotNull(config);
        Assert.Equal("http://localhost:8080/realms/test", config.OidcAuthority);
        Assert.Equal("test-client", config.OidcClientId);
        Assert.Equal("http://localhost:5000", config.ControlPlaneUrl);
        Assert.Equal(8888, config.CallbackPort);
        Assert.True(config.InsecureTls);
    }

    [Fact]
    public void JsonDeserialization_PartialConfig_UsesDefaults()
    {
        var json = """{ "controlPlaneUrl": "https://cp.example.com" }""";

        output.WriteLine($"Input: {json}");

        var config = JsonSerializer.Deserialize(json, CliJsonContext.Default.CliConfig);

        output.WriteLine($"  ControlPlaneUrl: {config!.ControlPlaneUrl}");
        output.WriteLine($"  OidcClientId:    {config.OidcClientId} (default)");
        output.WriteLine($"  CallbackPort:    {config.CallbackPort} (default)");

        Assert.NotNull(config);
        Assert.Equal("https://cp.example.com", config.ControlPlaneUrl);
        Assert.Equal("clustral-cli", config.OidcClientId);
        Assert.Equal(7777, config.CallbackPort);
    }

    [Fact]
    public void JsonDeserialization_EmptyObject_UsesDefaults()
    {
        var config = JsonSerializer.Deserialize("{}", CliJsonContext.Default.CliConfig);

        output.WriteLine("Input: {}");
        output.WriteLine($"  OidcClientId: {config!.OidcClientId} (default)");
        output.WriteLine($"  CallbackPort: {config.CallbackPort} (default)");

        Assert.NotNull(config);
        Assert.Equal("clustral-cli", config.OidcClientId);
        Assert.Equal(7777, config.CallbackPort);
    }
}
