namespace Clustral.ControlPlane.Domain.Repositories;

public interface IClusterRepository
{
    Task<Cluster?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
    Task<List<Cluster>> ListAsync(CancellationToken ct = default);
    Task InsertAsync(Cluster cluster, CancellationToken ct = default);
    Task ReplaceAsync(Cluster cluster, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
