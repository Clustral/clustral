using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure.Repositories;

public sealed class MongoAccessTokenRepository(ClustralDb db) : IAccessTokenRepository
{
    public async Task InsertAsync(AccessToken token, CancellationToken ct) =>
        await db.AccessTokens.InsertOneAsync(token, cancellationToken: ct);

    public async Task<AccessToken?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.AccessTokens.Find(t => t.Id == id).FirstOrDefaultAsync(ct);

    public async Task<AccessToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        await db.AccessTokens.Find(t => t.TokenHash == tokenHash).FirstOrDefaultAsync(ct);

    public async Task ReplaceAsync(AccessToken token, CancellationToken ct) =>
        await db.AccessTokens.ReplaceOneAsync(t => t.Id == token.Id, token, cancellationToken: ct);

    public async Task DeleteByClusterIdAsync(Guid clusterId, CancellationToken ct) =>
        await db.AccessTokens.DeleteManyAsync(t => t.ClusterId == clusterId, ct);

    public async Task DeleteByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.AccessTokens.DeleteManyAsync(t => t.UserId == userId, ct);
}
