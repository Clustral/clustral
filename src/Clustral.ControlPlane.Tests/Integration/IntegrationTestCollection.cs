namespace Clustral.ControlPlane.Tests.Integration;

/// <summary>
/// Shared test collection that ensures a single <see cref="ClustralWebApplicationFactory"/>
/// (and therefore a single MongoDB Testcontainer and Serilog bootstrap logger) is used
/// across all integration test classes. This avoids the "logger is already frozen"
/// Serilog error that occurs when multiple WebApplicationFactory instances are created.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<ClustralWebApplicationFactory>
{
    public const string Name = "Integration";
}
