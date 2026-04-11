using Clustral.ControlPlane.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Clustral.ControlPlane.Tests.Helpers;

/// <summary>
/// Shared Testcontainers MongoDB instance for unit tests that need a
/// <see cref="ClustralDb"/> (e.g., event handler publish tests).
/// Each call to <see cref="CreateDb"/> returns a context backed by an
/// isolated database.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder()
        .WithImage("mongo:8")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ClustralDb CreateDb()
    {
        var client = new MongoClient(ConnectionString);
        var options = Options.Create(new MongoDbOptions
        {
            DatabaseName = $"test-{Guid.NewGuid():N}",
        });
        return new ClustralDb(client, options);
    }

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("Mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>;
