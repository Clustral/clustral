using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure.Repositories;

public sealed class MongoAccessRequestRepository(ClustralDb db) : IAccessRequestRepository
{
    public async Task<AccessRequest?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.AccessRequests.Find(r => r.Id == id).FirstOrDefaultAsync(ct);

    public async Task<List<AccessRequest>> FindAsync(
        FilterDefinition<AccessRequest> filter, int limit, CancellationToken ct) =>
        await db.AccessRequests.Find(filter).SortByDescending(r => r.CreatedAt).Limit(limit).ToListAsync(ct);

    public async Task InsertAsync(AccessRequest request, CancellationToken ct) =>
        await db.AccessRequests.InsertOneAsync(request, cancellationToken: ct);

    public async Task ReplaceAsync(AccessRequest request, CancellationToken ct) =>
        await db.AccessRequests.ReplaceOneAsync(r => r.Id == request.Id, request, cancellationToken: ct);

    public async Task<long> ExpirePendingAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var result = await db.AccessRequests.UpdateManyAsync(
            r => r.Status == AccessRequestStatus.Pending && r.RequestExpiresAt <= cutoff,
            Builders<AccessRequest>.Update.Set(r => r.Status, AccessRequestStatus.Expired),
            cancellationToken: ct);
        return result.ModifiedCount;
    }

    public async Task<long> ExpireGrantsAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var result = await db.AccessRequests.UpdateManyAsync(
            r => r.Status == AccessRequestStatus.Approved
                 && r.GrantExpiresAt <= cutoff
                 && r.RevokedAt == null,
            Builders<AccessRequest>.Update.Set(r => r.Status, AccessRequestStatus.Expired),
            cancellationToken: ct);
        return result.ModifiedCount;
    }
}
