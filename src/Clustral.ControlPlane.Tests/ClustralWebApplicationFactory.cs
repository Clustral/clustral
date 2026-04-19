using Clustral.ControlPlane.Infrastructure.Redis;
using Clustral.ControlPlane.Infrastructure.Tunnel;
using Clustral.ControlPlane.Tests.Helpers;
using Clustral.V1;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Clustral.ControlPlane.Tests;

/// <summary>
/// Shared <see cref="WebApplicationFactory{TEntryPoint}"/> for integration tests.
/// Spins up a real MongoDB via Testcontainers and replaces JWT auth with
/// <see cref="TestAuthHandler"/>. Each test class gets its own isolated database.
/// </summary>
public sealed class ClustralWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder()
        .WithImage("mongo:8")
        .Build();

    public string MongoConnectionString => _mongo.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Provide required config values so StartupConfigValidator doesn't abort.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoDB:DatabaseName"] = "clustral-test",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace MongoDB client with one pointing at the Testcontainer.
            services.RemoveAll<IMongoClient>();
            services.AddSingleton<IMongoClient>(_ => new MongoClient(MongoConnectionString));

            // Replace authentication with our test handler.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // Override the default authentication scheme.
            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            // Register KubeconfigJwtService with a test key pair.
            using var testKey = System.Security.Cryptography.ECDsa.Create(
                System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            services.AddSingleton(Clustral.Sdk.Auth.KubeconfigJwtService.ForSigning(
                testKey.ExportECPrivateKeyPem()));

            // Replace Redis + TunnelProxy with no-op stubs for tests.
            services.RemoveAll<IRedisSessionRegistry>();
            services.AddSingleton<IRedisSessionRegistry>(new NoOpRedisSessionRegistry());
            services.RemoveAll<ITunnelProxyClient>();
            services.AddSingleton<ITunnelProxyClient>(new NoOpTunnelProxyClient());

            // Remove MassTransit's hosted service so bus startup doesn't
            // block waiting for a RabbitMQ connection that doesn't exist.
            var mtHostedServices = services
                .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                    && d.ImplementationType?.FullName?.StartsWith("MassTransit") == true)
                .ToList();
            foreach (var d in mtHostedServices)
                services.Remove(d);

            // Replace the real bus with a no-op so IPublishEndpoint injections
            // don't fail. Event handlers will publish but messages go nowhere.
            services.RemoveAll<IPublishEndpoint>();
            services.AddSingleton<IPublishEndpoint>(
                _ => new NoOpPublishEndpoint());
        });
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with a pre-set Bearer token and
    /// test claims. Use custom headers to override the default identity:
    /// <c>X-Test-Sub</c>, <c>X-Test-Email</c>, <c>X-Test-Name</c>.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(
        string sub   = TestAuthHandler.DefaultSub,
        string email = TestAuthHandler.DefaultEmail,
        string name  = TestAuthHandler.DefaultName)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        client.DefaultRequestHeaders.Add("X-Test-Sub", sub);
        client.DefaultRequestHeaders.Add("X-Test-Email", email);
        client.DefaultRequestHeaders.Add("X-Test-Name", name);
        return client;
    }

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _mongo.DisposeAsync();
    }

    /// <summary>No-op Redis registry — always returns null (no tunnel session found).</summary>
    private sealed class NoOpRedisSessionRegistry : IRedisSessionRegistry
    {
        public Task<string?> LookupSessionAsync(Guid clusterId, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    /// <summary>No-op tunnel proxy client — throws if called (should not be reached in tests).</summary>
    private sealed class NoOpTunnelProxyClient : ITunnelProxyClient
    {
        public Task<HttpResponseFrame> ProxyRequestAsync(
            string tunnelPod, Guid clusterId, HttpRequestFrame frame, CancellationToken ct)
            => throw new InvalidOperationException("TunnelProxy should not be called in tests");
    }
}
