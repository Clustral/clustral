using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Clustral.Sdk.Crypto;
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

    private string? _caCertPath;
    private string? _caKeyPath;
    private string? _tempCaDir;

    public string MongoConnectionString => _mongo.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Generate a test CA for mTLS tests.
        _tempCaDir = Path.Combine(Path.GetTempPath(), $"clustral-test-ca-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempCaDir);
        _caCertPath = Path.Combine(_tempCaDir, "ca.crt");
        _caKeyPath = Path.Combine(_tempCaDir, "ca.key");
        GenerateTestCA(_caCertPath, _caKeyPath);

        // Provide required config values so StartupConfigValidator doesn't abort.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoDB:DatabaseName"] = "clustral-test",
                ["Oidc:Authority"] = "http://localhost:8080/realms/clustral",
                ["Oidc:ClientId"] = "clustral-control-plane",
                ["Oidc:Audience"] = "clustral-control-plane",
                ["Oidc:RequireHttpsMetadata"] = "false",
                ["CertificateAuthority:CaCertPath"] = _caCertPath,
                ["CertificateAuthority:CaKeyPath"] = _caKeyPath,
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

        if (_tempCaDir is not null)
        {
            try { Directory.Delete(_tempCaDir, recursive: true); }
            catch { /* cleanup best-effort */ }
        }
    }

    private static void GenerateTestCA(string certPath, string keyPath)
    {
        using var caKey = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Clustral Test CA",
            caKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        using var caCert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(10));

        File.WriteAllText(certPath, caCert.ExportCertificatePem());
        File.WriteAllText(keyPath, caKey.ExportPkcs8PrivateKeyPem());
    }
}
