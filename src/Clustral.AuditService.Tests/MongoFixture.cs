using Clustral.AuditService.Infrastructure;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Clustral.AuditService.Tests;

/// <summary>
/// Shared Testcontainers MongoDB instance reused across all consumer and API
/// test classes via the <c>[Collection("Mongo")]</c> attribute. Each test
/// method gets an isolated database through <see cref="CreateDbContext"/>.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder()
        .WithImage("mongo:8")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Creates a fresh <see cref="AuditDbContext"/> backed by a unique database
    /// so tests never interfere with each other.
    /// </summary>
    public AuditDbContext CreateDbContext()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDB:DatabaseName"] = $"test-{Guid.NewGuid():N}",
            })
            .Build();

        return new AuditDbContext(new MongoClient(ConnectionString), config);
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("Mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>;
