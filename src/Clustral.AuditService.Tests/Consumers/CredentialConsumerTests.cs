using Clustral.AuditService.Consumers;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Contracts.IntegrationEvents;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Clustral.AuditService.Tests.Consumers;

[Collection("Mongo")]
public sealed class CredentialConsumerTests(MongoFixture mongo, ITestOutputHelper output)
{
    [Fact]
    public async Task IssuedConsumer_PersistsAuditEvent_WithCredentialDetails()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<CredentialIssuedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var credentialId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(2);

        await harness.Bus.Publish(new CredentialIssuedEvent
        {
            CredentialId = credentialId,
            UserId = userId,
            UserEmail = "alice@example.com",
            ClusterId = clusterId,
            ClusterName = "prod",
            ExpiresAt = expiresAt,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<CredentialIssuedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.CredentialIssued);
        stored.Event.Should().Be("credential.issued");
        stored.Category.Should().Be("credentials");
        stored.Severity.Should().Be(Severity.Info);
        stored.Success.Should().BeTrue();
        stored.User.Should().Be("alice@example.com");
        stored.UserId.Should().Be(userId);
        stored.ResourceType.Should().Be("Credential");
        stored.ResourceId.Should().Be(credentialId);
        stored.ClusterId.Should().Be(clusterId);
        stored.ClusterName.Should().Be("prod");
        stored.Message.Should().Contain("issued");
    }

    [Fact]
    public async Task RevokedConsumer_PersistsAuditEvent_WithReason()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<CredentialRevokedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var credentialId = Guid.NewGuid();

        await harness.Bus.Publish(new CredentialRevokedEvent
        {
            CredentialId = credentialId,
            Reason = "User departed",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<CredentialRevokedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        output.WriteLine($"Stored event code: {stored?.Code}");

        stored.Should().NotBeNull();
        stored!.Code.Should().Be(EventCodes.CredentialRevoked);
        stored.Event.Should().Be("credential.revoked");
        stored.Category.Should().Be("credentials");
        stored.Severity.Should().Be(Severity.Info);
        stored.ResourceType.Should().Be("Credential");
        stored.ResourceId.Should().Be(credentialId);
        stored.Message.Should().Contain("User departed");
    }

    [Fact]
    public async Task RevokedConsumer_WithoutReason_OmitsReasonFromMessage()
    {
        var db = mongo.CreateDbContext();
        await using var provider = BuildProvider<CredentialRevokedConsumer>(db);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var credentialId = Guid.NewGuid();

        await harness.Bus.Publish(new CredentialRevokedEvent
        {
            CredentialId = credentialId,
            Reason = null,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        (await harness.Consumed.Any<CredentialRevokedEvent>()).Should().BeTrue();

        var stored = await db.AuditEvents.Find(_ => true).FirstOrDefaultAsync();
        stored.Should().NotBeNull();
        stored!.Message.Should().NotContain(":");
    }

    private static ServiceProvider BuildProvider<TConsumer>(AuditDbContext db)
        where TConsumer : class, IConsumer
    {
        return new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<TConsumer>())
            .BuildServiceProvider(true);
    }
}
