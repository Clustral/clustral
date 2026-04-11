using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Shared;

/// <summary>
/// Integration tests that verify the app fails to start with invalid config
/// and starts successfully with valid config. Uses WebApplicationFactory
/// with config overrides — no real MongoDB or OIDC provider needed for
/// validation-only tests.
/// </summary>
public sealed class StartupConfigValidatorIntegrationTests(ITestOutputHelper output)
{
    /// <summary>
    /// Creates a WebApplicationFactory with the given config overrides and a
    /// stub IMongoClient so the health check doesn't try to connect to a real
    /// MongoDB instance and timeout.
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactoryWithConfig(
        Dictionary<string, string?> config)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, c) =>
                    c.AddInMemoryCollection(config));

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IMongoClient>();
                    services.AddSingleton<IMongoClient>(
                        new MongoClient("mongodb://localhost:27017/?serverSelectionTimeout=1s&connectTimeout=1s"));

                    // Register KubeconfigJwtService with a test key.
                    using var k = System.Security.Cryptography.ECDsa.Create(
                        System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
                    services.AddSingleton(Clustral.Sdk.Auth.KubeconfigJwtService.ForSigning(
                        k.ExportECPrivateKeyPem()));
                });
            });
    }

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        ["MongoDB:ConnectionString"] = "mongodb://localhost:27017/?serverSelectionTimeout=1s",
        ["MongoDB:DatabaseName"] = "test",
    };

    [Fact]
    public void Startup_InvalidMongoConnectionString_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            var config = ValidConfig();
            config["MongoDB:ConnectionString"] = "not-a-mongodb-url";

            using var factory = CreateFactoryWithConfig(config);
            factory.CreateClient();
        });

        output.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
        ex.Message.Should().ContainAny("ConnectionString", "connection string", "mongodb://");
    }

    [Fact]
    public void Startup_MaxTtlLessThanDefault_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            var config = ValidConfig();
            config["Credential:DefaultKubeconfigCredentialTtl"] = "08:00:00";
            config["Credential:MaxKubeconfigCredentialTtl"] = "01:00:00";

            using var factory = CreateFactoryWithConfig(config);
            factory.CreateClient();
        });

        output.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
        ex.Message.Should().Contain("MaxKubeconfigCredentialTtl");
    }

    [Fact]
    public void Startup_ValidConfig_StartsSuccessfully()
    {
        // Use the shared factory from integration tests — it has valid config.
        using var factory = new ClustralWebApplicationFactory();
        factory.InitializeAsync().GetAwaiter().GetResult();

        var client = factory.CreateClient();
        var response = client.GetAsync("/healthz").GetAwaiter().GetResult();

        output.WriteLine($"Healthz: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
