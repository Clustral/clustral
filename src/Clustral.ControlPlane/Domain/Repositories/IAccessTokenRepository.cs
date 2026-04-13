namespace Clustral.ControlPlane.Domain.Repositories;

public interface IAccessTokenRepository
{
    Task InsertAsync(AccessToken token, CancellationToken ct = default);
    Task<AccessToken?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AccessToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task ReplaceAsync(AccessToken token, CancellationToken ct = default);
    Task DeleteByClusterIdAsync(Guid clusterId, CancellationToken ct = default);
    Task DeleteByUserIdAsync(Guid userId, CancellationToken ct = default);
}
