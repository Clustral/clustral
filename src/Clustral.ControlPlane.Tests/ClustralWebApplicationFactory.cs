using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
}
