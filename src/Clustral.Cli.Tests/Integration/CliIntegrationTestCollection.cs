using Clustral.ControlPlane.Tests;

namespace Clustral.Cli.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class CliIntegrationTestCollection : ICollectionFixture<ClustralWebApplicationFactory>
{
    public const string Name = "CLI Integration";
}
