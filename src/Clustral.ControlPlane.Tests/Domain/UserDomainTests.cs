using Clustral.ControlPlane.Domain;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Domain;

public sealed class UserDomainTests(ITestOutputHelper output)
{
    [Fact]
    public void UpdateFromOidcClaims_SetsEmailAndDisplayName()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            KeycloakSubject = "sub-123",
            Email = "old@test.com",
            DisplayName = "Old Name",
        };

        user.UpdateFromOidcClaims("new@test.com", "New Name");

        output.WriteLine($"Email: {user.Email}, Name: {user.DisplayName}");
        user.Email.Should().Be("new@test.com");
        user.DisplayName.Should().Be("New Name");
        user.LastSeenAt.Should().NotBeNull();
        user.LastSeenAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void UpdateFromOidcClaims_NullValues_ClearsFields()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            KeycloakSubject = "sub-123",
            Email = "existing@test.com",
            DisplayName = "Existing",
        };

        user.UpdateFromOidcClaims(null, null);

        user.Email.Should().BeNull();
        user.DisplayName.Should().BeNull();
        user.LastSeenAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateFromOidcClaims_PreservesImmutableFields()
    {
        var id = Guid.NewGuid();
        var user = new User
        {
            Id = id,
            KeycloakSubject = "sub-123",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };
        var originalCreatedAt = user.CreatedAt;

        user.UpdateFromOidcClaims("new@test.com", "Name");

        user.Id.Should().Be(id);
        user.KeycloakSubject.Should().Be("sub-123");
        user.CreatedAt.Should().Be(originalCreatedAt);
    }
}
