using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure.Repositories;

public sealed class MongoClusterRepository(ClustralDb db) : IClusterRepository
{
    public async Task<Cluster?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Clusters.Find(c => c.Id == id).FirstOrDefaultAsync(ct);

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct) =>
        await db.Clusters.Find(c => c.Name == name).AnyAsync(ct);

    public async Task<List<Cluster>> ListAsync(CancellationToken ct) =>
        await db.Clusters.Find(_ => true).ToListAsync(ct);

    public async Task InsertAsync(Cluster cluster, CancellationToken ct) =>
        await db.Clusters.InsertOneAsync(cluster, cancellationToken: ct);

    public async Task ReplaceAsync(Cluster cluster, CancellationToken ct) =>
        await db.Clusters.ReplaceOneAsync(c => c.Id == cluster.Id, cluster, cancellationToken: ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await db.Clusters.DeleteOneAsync(c => c.Id == id, ct);
        return result.DeletedCount > 0;
    }
}
