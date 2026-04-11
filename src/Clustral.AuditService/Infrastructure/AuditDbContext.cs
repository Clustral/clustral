using Clustral.AuditService.Domain;
using MongoDB.Driver;

namespace Clustral.AuditService.Infrastructure;

/// <summary>
/// MongoDB context for the audit log database. Provides typed access
/// to the <c>audit_events</c> collection.
/// </summary>
public sealed class AuditDbContext
{
    public IMongoCollection<AuditEvent> AuditEvents { get; }

    public AuditDbContext(IMongoClient client, IConfiguration configuration)
    {
        var dbName = configuration["MongoDB:DatabaseName"] ?? "clustral-audit";
        var db = client.GetDatabase(dbName);
        AuditEvents = db.GetCollection<AuditEvent>("audit_events");
    }

    /// <summary>
    /// Creates indexes for efficient querying. Called once on startup.
    /// </summary>
    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        var indexModels = new List<CreateIndexModel<AuditEvent>>
        {
            // Time-based queries (newest first).
            new(Builders<AuditEvent>.IndexKeys.Descending(e => e.Time),
                new CreateIndexOptions { Name = "ix_audit_time" }),

            // Category filter.
            new(Builders<AuditEvent>.IndexKeys
                .Ascending(e => e.Category)
                .Descending(e => e.Time),
                new CreateIndexOptions { Name = "ix_audit_category_time" }),

            // User filter.
            new(Builders<AuditEvent>.IndexKeys
                .Ascending(e => e.User)
                .Descending(e => e.Time),
                new CreateIndexOptions { Name = "ix_audit_user_time" }),

            // Code filter.
            new(Builders<AuditEvent>.IndexKeys.Ascending(e => e.Code),
                new CreateIndexOptions { Name = "ix_audit_code" }),

            // Cluster filter.
            new(Builders<AuditEvent>.IndexKeys
                .Ascending(e => e.ClusterId)
                .Descending(e => e.Time),
                new CreateIndexOptions { Name = "ix_audit_cluster_time" }),

            // Resource filter.
            new(Builders<AuditEvent>.IndexKeys
                .Ascending(e => e.ResourceId)
                .Descending(e => e.Time),
                new CreateIndexOptions { Name = "ix_audit_resource_time" }),
        };

        await AuditEvents.Indexes.CreateManyAsync(indexModels, ct);
    }
}
