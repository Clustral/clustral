using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Features.AccessRequests;
using Clustral.ControlPlane.Features.Auth;
using Clustral.ControlPlane.Features.Clusters;
using Clustral.ControlPlane.Features.Roles;
using Clustral.ControlPlane.Features.Proxy;
using Clustral.ControlPlane.Features.Users;
using Clustral.ControlPlane.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Shared;

/// <summary>
/// Integration tests that verify event handlers enrich integration events
/// with user emails, cluster names, and role names by looking up entities
/// in MongoDB. Each test seeds the DB, fires a domain event, and asserts
/// the published integration event carries the enriched fields.
/// </summary>
[Collection("Mongo")]
public sealed class EventEnrichmentIntegrationTests(MongoFixture mongo, ITestOutputHelper output)
{
    // ── Proxy ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProxyRequestCompleted_EnrichesUserEmailAndClusterName()
    {
        var db = mongo.CreateDb();
        var userId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = userId,
            KeycloakSubject = "sub-proxy",
            Email = "alice@example.com",
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("prod-east", "Production East", "fakehash"));

        // Re-read to get the generated cluster ID
        var cluster = await db.Clusters.Find(c => c.Name == "prod-east").FirstAsync();

        var published = new List<object>();
        var handler = new ProxyAuditHandler(
            NullLogger<ProxyAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new ProxyRequestCompleted(
            cluster.Id, userId, Guid.NewGuid(),
            "GET", "/api/v1/pods", 200, 42.5), CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ProxyRequestCompletedEvent>().Subject;

        output.WriteLine($"UserEmail={evt.UserEmail}, ClusterName={evt.ClusterName}");

        evt.UserEmail.Should().Be("alice@example.com");
        evt.ClusterName.Should().Be("prod-east");
        evt.UserId.Should().Be(userId);
        evt.ClusterId.Should().Be(cluster.Id);
        evt.Method.Should().Be("GET");
        evt.Path.Should().Be("/api/v1/pods");
        evt.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ProxyRequestCompleted_WithMissingUser_PublishesWithNullEmail()
    {
        var db = mongo.CreateDb();

        var published = new List<object>();
        var handler = new ProxyAuditHandler(
            NullLogger<ProxyAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new ProxyRequestCompleted(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "POST", "/api/v1/deployments", 201, 100.0), CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ProxyRequestCompletedEvent>().Subject;

        evt.UserEmail.Should().BeNull();
        evt.ClusterName.Should().BeNull();
    }

    [Fact]
    public async Task ProxyAccessDenied_EnrichesUserEmailAndClusterName()
    {
        var db = mongo.CreateDb();
        var userId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = userId, KeycloakSubject = "sub-denied", Email = "denied@example.com",
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("denied-cluster", "Denied", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "denied-cluster").FirstAsync();

        var published = new List<object>();
        var handler = new ProxyAuditHandler(
            NullLogger<ProxyAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new ProxyAccessDenied(
            cluster.Id, userId, "GET", "/api/v1/pods",
            "No role assigned for this cluster."), CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ProxyAccessDeniedEvent>().Subject;

        output.WriteLine($"UserEmail={evt.UserEmail}, ClusterName={evt.ClusterName}, Reason={evt.Reason}");

        evt.UserEmail.Should().Be("denied@example.com");
        evt.ClusterName.Should().Be("denied-cluster");
        evt.Reason.Should().Contain("No role assigned");
        evt.Method.Should().Be("GET");
        evt.Path.Should().Be("/api/v1/pods");
    }

    [Fact]
    public async Task ProxyAccessDenied_WithNullUserId_PublishesWithNullEmail()
    {
        var db = mongo.CreateDb();

        var published = new List<object>();
        var handler = new ProxyAuditHandler(
            NullLogger<ProxyAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new ProxyAccessDenied(
            Guid.NewGuid(), null, "GET", "/api/v1/nodes",
            "Invalid credential"), CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ProxyAccessDeniedEvent>().Subject;

        evt.UserEmail.Should().BeNull();
        evt.UserId.Should().BeNull();
    }

    // ── Access Requests ────────────────────────────────────────────────────

    [Fact]
    public async Task AccessRequestCreated_EnrichesRequesterEmailAndRoleNameAndClusterName()
    {
        var db = mongo.CreateDb();
        var requesterId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = requesterId, KeycloakSubject = "sub-req", Email = "requester@example.com",
        });
        await db.Roles.InsertOneAsync(new Role
        {
            Id = roleId, Name = "developer", KubernetesGroups = ["dev-team"],
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("staging", "Staging cluster", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "staging").FirstAsync();

        var published = new List<object>();
        var handler = new AccessRequestAuditHandler(
            NullLogger<AccessRequestAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new AccessRequestCreated(
            Guid.NewGuid(), requesterId, roleId, cluster.Id,
            "Need access", TimeSpan.FromHours(4)), CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<AccessRequestCreatedEvent>().Subject;

        output.WriteLine($"RequesterEmail={evt.RequesterEmail}, RoleName={evt.RoleName}, ClusterName={evt.ClusterName}");

        evt.RequesterEmail.Should().Be("requester@example.com");
        evt.RoleName.Should().Be("developer");
        evt.ClusterName.Should().Be("staging");
    }

    [Fact]
    public async Task AccessRequestDenied_EnrichesReviewerEmailAndCluster()
    {
        var db = mongo.CreateDb();
        var reviewerId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = reviewerId, KeycloakSubject = "sub-rev", Email = "reviewer@example.com",
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("prod", "Production", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "prod").FirstAsync();

        // Seed the access request so the handler can look up the cluster
        await db.AccessRequests.InsertOneAsync(new AccessRequest
        {
            Id = requestId, RequesterId = Guid.NewGuid(), RoleId = Guid.NewGuid(),
            ClusterId = cluster.Id, Status = AccessRequestStatus.Pending,
            Reason = "test", RequestedDuration = TimeSpan.FromHours(1),
        });

        var published = new List<object>();
        var handler = new AccessRequestAuditHandler(
            NullLogger<AccessRequestAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new AccessRequestDenied(requestId, reviewerId, "Not justified"),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<AccessRequestDeniedEvent>().Subject;

        output.WriteLine($"ReviewerEmail={evt.ReviewerEmail}, ClusterName={evt.ClusterName}");

        evt.ReviewerEmail.Should().Be("reviewer@example.com");
        evt.ClusterId.Should().Be(cluster.Id);
        evt.ClusterName.Should().Be("prod");
    }

    // ── Credentials ────────────────────────────────────────────────────────

    [Fact]
    public async Task CredentialIssued_EnrichesUserEmailAndClusterName()
    {
        var db = mongo.CreateDb();
        var userId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = userId, KeycloakSubject = "sub-cred", Email = "cred-user@example.com",
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("dev", "Dev cluster", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "dev").FirstAsync();

        var published = new List<object>();
        var handler = new CredentialAuditHandler(
            NullLogger<CredentialAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new CredentialIssued(
            Guid.NewGuid(), userId, cluster.Id, DateTimeOffset.UtcNow.AddHours(8)),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<CredentialIssuedEvent>().Subject;

        output.WriteLine($"UserEmail={evt.UserEmail}, ClusterName={evt.ClusterName}");

        evt.UserEmail.Should().Be("cred-user@example.com");
        evt.ClusterName.Should().Be("dev");
    }

    [Fact]
    public async Task CredentialRevoked_EnrichesUserEmailAndClusterName()
    {
        var db = mongo.CreateDb();
        var userId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = userId, KeycloakSubject = "sub-rev-cred", Email = "revoked@example.com",
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("prod-west", "Prod West", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "prod-west").FirstAsync();

        await db.AccessTokens.InsertOneAsync(new AccessToken
        {
            Id = credentialId, UserId = userId, ClusterId = cluster.Id,
            TokenHash = "fakehash", Kind = CredentialKind.UserKubeconfig,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        });

        var published = new List<object>();
        var handler = new CredentialAuditHandler(
            NullLogger<CredentialAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new CredentialRevoked(credentialId, "User offboarded"),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<CredentialRevokedEvent>().Subject;

        output.WriteLine($"UserEmail={evt.UserEmail}, ClusterName={evt.ClusterName}");

        evt.UserEmail.Should().Be("revoked@example.com");
        evt.ClusterId.Should().Be(cluster.Id);
        evt.ClusterName.Should().Be("prod-west");
    }

    // ── Clusters ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ClusterConnected_EnrichesClusterName()
    {
        var db = mongo.CreateDb();
        await db.Clusters.InsertOneAsync(Cluster.Create("k8s-east", "K8s East", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "k8s-east").FirstAsync();

        var published = new List<object>();
        var handler = new ClusterAuditHandler(
            NullLogger<ClusterAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new ClusterConnected(cluster.Id, "v1.30.2"),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ClusterConnectedEvent>().Subject;

        output.WriteLine($"ClusterName={evt.ClusterName}");

        evt.ClusterName.Should().Be("k8s-east");
        evt.KubernetesVersion.Should().Be("v1.30.2");
    }

    // ── Roles ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoleDeleted_EnrichesRoleName()
    {
        var db = mongo.CreateDb();
        var roleId = Guid.NewGuid();
        await db.Roles.InsertOneAsync(new Role
        {
            Id = roleId, Name = "obsolete-role", KubernetesGroups = [],
        });

        var published = new List<object>();
        var handler = new RoleAuditHandler(
            NullLogger<RoleAuditHandler>.Instance,
            new FakePublishEndpoint(published));

        await handler.Handle(new RoleDeleted(roleId, "obsolete-role"), CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<RoleDeletedEvent>().Subject;

        output.WriteLine($"Name={evt.Name}");

        evt.Name.Should().Be("obsolete-role");
    }

    // ── Users ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoleAssigned_EnrichesUserEmailAndRoleNameAndClusterName()
    {
        var db = mongo.CreateDb();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = userId, KeycloakSubject = "sub-assign", Email = "assigned@example.com",
        });
        await db.Roles.InsertOneAsync(new Role
        {
            Id = roleId, Name = "admin", KubernetesGroups = ["system:masters"],
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("assign-cluster", "Assign Cluster", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "assign-cluster").FirstAsync();

        var published = new List<object>();
        var handler = new UserAuditHandler(
            NullLogger<UserAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new RoleAssigned(userId, roleId, cluster.Id, "admin@corp.com"),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<RoleAssignedEvent>().Subject;

        output.WriteLine($"UserEmail={evt.UserEmail}, RoleName={evt.RoleName}, ClusterName={evt.ClusterName}");

        evt.UserEmail.Should().Be("assigned@example.com");
        evt.RoleName.Should().Be("admin");
        evt.ClusterName.Should().Be("assign-cluster");
    }

    [Fact]
    public async Task RoleUnassigned_EnrichesAllFieldsFromAssignment()
    {
        var db = mongo.CreateDb();
        var assignmentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = userId, KeycloakSubject = "sub-unassign", Email = "unassigned@example.com",
        });
        await db.Roles.InsertOneAsync(new Role
        {
            Id = roleId, Name = "viewer", KubernetesGroups = ["viewers"],
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("unassign-cluster", "Unassign Cluster", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "unassign-cluster").FirstAsync();

        await db.RoleAssignments.InsertOneAsync(new RoleAssignment
        {
            Id = assignmentId, UserId = userId, RoleId = roleId,
            ClusterId = cluster.Id, AssignedBy = "admin@corp.com",
        });

        var published = new List<object>();
        var handler = new UserAuditHandler(
            NullLogger<UserAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new RoleUnassigned(assignmentId, userId, roleId, cluster.Id),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<RoleUnassignedEvent>().Subject;

        output.WriteLine($"UserEmail={evt.UserEmail}, RoleName={evt.RoleName}, ClusterName={evt.ClusterName}");

        evt.UserId.Should().Be(userId);
        evt.UserEmail.Should().Be("unassigned@example.com");
        evt.RoleId.Should().Be(roleId);
        evt.RoleName.Should().Be("viewer");
        evt.ClusterId.Should().Be(cluster.Id);
        evt.ClusterName.Should().Be("unassign-cluster");
    }

    // ── Access Request — remaining events ──────────────────────────────────

    [Fact]
    public async Task AccessRequestApproved_EnrichesReviewerEmailAndCluster()
    {
        var db = mongo.CreateDb();
        var reviewerId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = reviewerId, KeycloakSubject = "sub-approve", Email = "approver@example.com",
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("approve-cluster", "Approve", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "approve-cluster").FirstAsync();

        await db.AccessRequests.InsertOneAsync(new AccessRequest
        {
            Id = requestId, RequesterId = Guid.NewGuid(), RoleId = Guid.NewGuid(),
            ClusterId = cluster.Id, Status = AccessRequestStatus.Approved,
            Reason = "test", RequestedDuration = TimeSpan.FromHours(1),
        });

        var published = new List<object>();
        var handler = new AccessRequestAuditHandler(
            NullLogger<AccessRequestAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new AccessRequestApproved(
            requestId, reviewerId, TimeSpan.FromHours(4),
            DateTimeOffset.UtcNow.AddHours(4)), CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<AccessRequestApprovedEvent>().Subject;

        output.WriteLine($"ReviewerEmail={evt.ReviewerEmail}, ClusterName={evt.ClusterName}");

        evt.ReviewerEmail.Should().Be("approver@example.com");
        evt.ClusterId.Should().Be(cluster.Id);
        evt.ClusterName.Should().Be("approve-cluster");
    }

    [Fact]
    public async Task AccessRequestRevoked_EnrichesRevokerEmailAndCluster()
    {
        var db = mongo.CreateDb();
        var revokerId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = revokerId, KeycloakSubject = "sub-revoker", Email = "revoker@example.com",
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("revoke-cluster", "Revoke", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "revoke-cluster").FirstAsync();

        await db.AccessRequests.InsertOneAsync(new AccessRequest
        {
            Id = requestId, RequesterId = Guid.NewGuid(), RoleId = Guid.NewGuid(),
            ClusterId = cluster.Id, Status = AccessRequestStatus.Approved,
            Reason = "test", RequestedDuration = TimeSpan.FromHours(1),
        });

        var published = new List<object>();
        var handler = new AccessRequestAuditHandler(
            NullLogger<AccessRequestAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new AccessRequestRevoked(requestId, revokerId, "Policy change"),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<AccessRequestRevokedEvent>().Subject;

        evt.RevokedByEmail.Should().Be("revoker@example.com");
        evt.ClusterId.Should().Be(cluster.Id);
        evt.ClusterName.Should().Be("revoke-cluster");
    }

    [Fact]
    public async Task AccessRequestExpired_EnrichesRequesterAndCluster()
    {
        var db = mongo.CreateDb();
        var requesterId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await db.Users.InsertOneAsync(new User
        {
            Id = requesterId, KeycloakSubject = "sub-expired", Email = "expired@example.com",
        });
        await db.Clusters.InsertOneAsync(Cluster.Create("expire-cluster", "Expire", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "expire-cluster").FirstAsync();

        await db.AccessRequests.InsertOneAsync(new AccessRequest
        {
            Id = requestId, RequesterId = requesterId, RoleId = Guid.NewGuid(),
            ClusterId = cluster.Id, Status = AccessRequestStatus.Approved,
            Reason = "test", RequestedDuration = TimeSpan.FromHours(1),
        });

        var published = new List<object>();
        var handler = new AccessRequestAuditHandler(
            NullLogger<AccessRequestAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new AccessRequestExpired(requestId),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<AccessRequestExpiredEvent>().Subject;

        evt.RequesterEmail.Should().Be("expired@example.com");
        evt.ClusterId.Should().Be(cluster.Id);
        evt.ClusterName.Should().Be("expire-cluster");
    }

    // ── Clusters — remaining events ────────────────────────────────────────

    [Fact]
    public async Task ClusterRegistered_PublishesWithName()
    {
        var db = mongo.CreateDb();
        var published = new List<object>();
        var handler = new ClusterAuditHandler(
            NullLogger<ClusterAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        var clusterId = Guid.NewGuid();

        await handler.Handle(new ClusterRegistered(clusterId, "new-cluster"),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ClusterRegisteredEvent>().Subject;

        evt.ClusterId.Should().Be(clusterId);
        evt.Name.Should().Be("new-cluster");
    }

    [Fact]
    public async Task ClusterDisconnected_EnrichesClusterName()
    {
        var db = mongo.CreateDb();
        await db.Clusters.InsertOneAsync(Cluster.Create("dc-cluster", "DC", "fakehash"));
        var cluster = await db.Clusters.Find(c => c.Name == "dc-cluster").FirstAsync();

        var published = new List<object>();
        var handler = new ClusterAuditHandler(
            NullLogger<ClusterAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        await handler.Handle(new ClusterDisconnected(cluster.Id),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ClusterDisconnectedEvent>().Subject;

        evt.ClusterId.Should().Be(cluster.Id);
        evt.ClusterName.Should().Be("dc-cluster");
    }

    [Fact]
    public async Task ClusterDeleted_PublishesWithNameFromDomainEvent()
    {
        var db = mongo.CreateDb();
        var published = new List<object>();
        var handler = new ClusterAuditHandler(
            NullLogger<ClusterAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        var clusterId = Guid.NewGuid();

        await handler.Handle(new ClusterDeleted(clusterId, "deleted-cluster"),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<ClusterDeletedEvent>().Subject;

        evt.ClusterId.Should().Be(clusterId);
        evt.ClusterName.Should().Be("deleted-cluster");
    }

    // ── Roles — remaining events ───────────────────────────────────────────

    [Fact]
    public async Task RoleCreated_PublishesWithName()
    {
        var db = mongo.CreateDb();
        var published = new List<object>();
        var handler = new RoleAuditHandler(
            NullLogger<RoleAuditHandler>.Instance,
            new FakePublishEndpoint(published));

        var roleId = Guid.NewGuid();

        await handler.Handle(new RoleCreated(roleId, "new-role", ["dev-team"]),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<RoleCreatedEvent>().Subject;

        evt.RoleId.Should().Be(roleId);
        evt.Name.Should().Be("new-role");
        evt.KubernetesGroups.Should().Contain("dev-team");
    }

    [Fact]
    public async Task RoleUpdated_PublishesWithName()
    {
        var db = mongo.CreateDb();
        var published = new List<object>();
        var handler = new RoleAuditHandler(
            NullLogger<RoleAuditHandler>.Instance,
            new FakePublishEndpoint(published));

        var roleId = Guid.NewGuid();

        await handler.Handle(new RoleUpdated(roleId, "updated-role", "New desc", ["admins"]),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<RoleUpdatedEvent>().Subject;

        evt.RoleId.Should().Be(roleId);
        evt.Name.Should().Be("updated-role");
    }

    // ── Auth — remaining events ────────────────────────────────────────────

    [Fact]
    public async Task UserSynced_PublishesWithEmail()
    {
        var db = mongo.CreateDb();
        var published = new List<object>();
        var handler = new UserAuditHandler(
            NullLogger<UserAuditHandler>.Instance,
            new FakePublishEndpoint(published), db);

        var userId = Guid.NewGuid();

        await handler.Handle(new UserSynced(userId, "sub-sync", "synced@example.com", true),
            CancellationToken.None);

        var evt = published.Should().ContainSingle()
            .Which.Should().BeOfType<UserSyncedEvent>().Subject;

        evt.UserId.Should().Be(userId);
        evt.Email.Should().Be("synced@example.com");
        evt.IsNew.Should().BeTrue();
    }
}
