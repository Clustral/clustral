namespace Clustral.E2E.Tests.Fixtures;

/// <summary>
/// xUnit collection definition that shares a single <see cref="E2EFixture"/>
/// across every E2E test class. This avoids paying the K3s + Keycloak +
/// MongoDB + image-build cost (~60s) more than once per test run.
///
/// Test classes opt in by adding <c>[Collection(E2ETestCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class E2ETestCollection : ICollectionFixture<E2EFixture>
{
    public const string Name = "E2E";
}
