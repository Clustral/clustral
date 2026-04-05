using Clustral.ControlPlane.Domain;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure;

/// <summary>
/// Provides typed access to the Clustral MongoDB collections and ensures
/// indexes are created on first use.
/// </summary>
public sealed class ClustralDb
{
    private readonly IMongoDatabase _database;

    public ClustralDb(IMongoClient client, string databaseName = "clustral")
    {
        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<Cluster>        Clusters        => _database.GetCollection<Cluster>("clusters");
    public IMongoCollection<User>           Users           => _database.GetCollection<User>("users");
    public IMongoCollection<AccessToken>    AccessTokens    => _database.GetCollection<AccessToken>("access_tokens");
    public IMongoCollection<Role>           Roles           => _database.GetCollection<Role>("roles");
    public IMongoCollection<RoleAssignment> RoleAssignments => _database.GetCollection<RoleAssignment>("role_assignments");

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
    }
}
