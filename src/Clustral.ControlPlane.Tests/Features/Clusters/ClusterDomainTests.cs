using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Events;
using FluentAssertions;
using Xunit;

namespace Clustral.ControlPlane.Tests.Features.Clusters;

public sealed class ClusterDomainTests
{
    [Fact]
    public void ConsumeBootstrapToken_ClearsHash()
    {
        var cluster = Cluster.Create("test", "desc", "pem-key", "token-hash-123");

        cluster.BootstrapTokenHash.Should().Be("token-hash-123");
        cluster.ConsumeBootstrapToken();
        cluster.BootstrapTokenHash.Should().BeNull();
    }

    [Fact]
    public void RevokeAgentCredentials_IncrementsTokenVersion()
    {
        var cluster = Cluster.Create("test", "desc", "pem-key", "hash");

        cluster.TokenVersion.Should().Be(1);
        cluster.RevokeAgentCredentials();
        cluster.TokenVersion.Should().Be(2);
        cluster.RevokeAgentCredentials();
        cluster.TokenVersion.Should().Be(3);
    }

    [Fact]
    public void RevokeAgentCredentials_RaisesDomainEvent()
    {
        var cluster = Cluster.Create("test", "desc", "pem-key", "hash");
        cluster.ClearDomainEvents(); // clear the ClusterRegistered event

        cluster.RevokeAgentCredentials();

        var events = cluster.DomainEvents;
        events.Should().ContainSingle()
            .Which.Should().BeOfType<AgentCredentialsRevoked>()
            .Which.NewTokenVersion.Should().Be(2);
    }

    [Fact]
    public void RecordCertificateFingerprint_StoresValue()
    {
        var cluster = Cluster.Create("test", "desc", "pem-key", "hash");

        cluster.CertificateFingerprint.Should().BeNull();
        cluster.RecordCertificateFingerprint("abc123def456");
        cluster.CertificateFingerprint.Should().Be("abc123def456");
    }
}
