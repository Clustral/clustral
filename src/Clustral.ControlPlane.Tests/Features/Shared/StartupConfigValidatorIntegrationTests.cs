using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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


    [Fact]
    public void Startup_MissingOidcAuthority_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Oidc:Authority"] = "",
                            ["Oidc:ClientId"] = "test-client",
                            ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
                            ["MongoDB:DatabaseName"] = "test",
                        });
                    });
                });

            // Force startup by creating a client.
            factory.CreateClient();
        });

        output.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
        ex.Message.Should().Contain("Authority");
    }

    [Fact]
    public void Startup_MissingOidcClientId_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Oidc:Authority"] = "http://localhost:8080/realms/test",
                            ["Oidc:ClientId"] = "",
                            ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
                            ["MongoDB:DatabaseName"] = "test",
                        });
                    });
                });

            factory.CreateClient();
        });

        output.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
        ex.Message.Should().Contain("ClientId");
    }

    [Fact]
    public void Startup_InvalidMongoConnectionString_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Oidc:Authority"] = "http://localhost:8080/realms/test",
                            ["Oidc:ClientId"] = "test-client",
                            ["MongoDB:ConnectionString"] = "not-a-mongodb-url",
                            ["MongoDB:DatabaseName"] = "test",
                        });
                    });
                });

            factory.CreateClient();
        });

        output.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
        ex.Message.Should().ContainAny("ConnectionString", "connection string", "mongodb://");
    }

    [Fact]
    public void Startup_InvalidOidcAuthorityUrl_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Oidc:Authority"] = "not-a-url",
                            ["Oidc:ClientId"] = "test-client",
                            ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
                            ["MongoDB:DatabaseName"] = "test",
                        });
                    });
                });

            factory.CreateClient();
        });

        output.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
        ex.Message.Should().Contain("Authority");
    }

    [Fact]
    public void Startup_MaxTtlLessThanDefault_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Oidc:Authority"] = "http://localhost:8080/realms/test",
                            ["Oidc:ClientId"] = "test-client",
                            ["Oidc:DefaultKubeconfigCredentialTtl"] = "08:00:00",
                            ["Oidc:MaxKubeconfigCredentialTtl"] = "01:00:00",
                            ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
                            ["MongoDB:DatabaseName"] = "test",
                        });
                    });
                });

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
