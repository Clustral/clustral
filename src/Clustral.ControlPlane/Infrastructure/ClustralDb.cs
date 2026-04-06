using Clustral.ControlPlane.Domain;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure;

/// <summary>
/// Provides typed access to the Clustral MongoDB collections and ensures
/// indexes are created on first use.
/// </summary>
public sealed class ClustralDb
{
    private readonly IMongoDatabase _database;

    public ClustralDb(IMongoClient client, IOptions<MongoDbOptions> options)
    {
        _database = client.GetDatabase(options.Value.DatabaseName);
    }

    public IMongoCollection<Cluster>        Clusters        => _database.GetCollection<Cluster>("clusters");
    public IMongoCollection<User>           Users           => _database.GetCollection<User>("users");
    public IMongoCollection<AccessToken>    AccessTokens    => _database.GetCollection<AccessToken>("access_tokens");
    public IMongoCollection<Role>           Roles           => _database.GetCollection<Role>("roles");
    public IMongoCollection<RoleAssignment> RoleAssignments => _database.GetCollection<RoleAssignment>("role_assignments");
    public IMongoCollection<AccessRequest>  AccessRequests  => _database.GetCollection<AccessRequest>("access_requests");

    /// <summary>
    /// Creates unique indexes required for correctness.
    /// Called once at startup.
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // clusters.name — unique
        await Clusters.Indexes.CreateOneAsync(
            new CreateIndexModel<Cluster>(
                Builders<Cluster>.IndexKeys.Ascending(c => c.Name),
                new CreateIndexOptions { Unique = true, Name = "ix_clusters_name" }));

        // users.keycloak_subject — unique
        await Users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.KeycloakSubject),
                new CreateIndexOptions { Unique = true, Name = "ix_users_keycloak_subject" }));

        // access_tokens.token_hash — unique
        await AccessTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<AccessToken>(
                Builders<AccessToken>.IndexKeys.Ascending(t => t.TokenHash),
                new CreateIndexOptions { Unique = true, Name = "ix_access_tokens_token_hash" }));

        // access_tokens.cluster_id — for lookups by cluster
        await AccessTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<AccessToken>(
                Builders<AccessToken>.IndexKeys.Ascending(t => t.ClusterId),
                new CreateIndexOptions { Name = "ix_access_tokens_cluster_id" }));

        // roles.name — unique
        await Roles.Indexes.CreateOneAsync(
            new CreateIndexModel<Role>(
                Builders<Role>.IndexKeys.Ascending(r => r.Name),
                new CreateIndexOptions { Unique = true, Name = "ix_roles_name" }));

        // role_assignments.(user_id, cluster_id) — unique, one role per user per cluster
        await RoleAssignments.Indexes.CreateOneAsync(
            new CreateIndexModel<RoleAssignment>(
                Builders<RoleAssignment>.IndexKeys
                    .Ascending(a => a.UserId)
                    .Ascending(a => a.ClusterId),
                new CreateIndexOptions { Unique = true, Name = "ix_role_assignments_user_cluster" }));

        // access_requests.(requester_id, status) — user's own requests
        await AccessRequests.Indexes.CreateOneAsync(
            new CreateIndexModel<AccessRequest>(
                Builders<AccessRequest>.IndexKeys
                    .Ascending(r => r.RequesterId)
                    .Ascending(r => r.Status),
                new CreateIndexOptions { Name = "ix_access_requests_requester_status" }));

        // access_requests.status — admin listing pending
        await AccessRequests.Indexes.CreateOneAsync(
            new CreateIndexModel<AccessRequest>(
                Builders<AccessRequest>.IndexKeys.Ascending(r => r.Status),
                new CreateIndexOptions { Name = "ix_access_requests_status" }));

        // access_requests.(requester_id, cluster_id, status, grant_expires_at) — proxy JIT lookup
        await AccessRequests.Indexes.CreateOneAsync(
            new CreateIndexModel<AccessRequest>(
                Builders<AccessRequest>.IndexKeys
                    .Ascending(r => r.RequesterId)
                    .Ascending(r => r.ClusterId)
                    .Ascending(r => r.Status)
                    .Ascending(r => r.GrantExpiresAt),
                new CreateIndexOptions { Name = "ix_access_requests_grant_lookup" }));
    }
}
