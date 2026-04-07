using MongoDB.Driver;

namespace Clustral.ControlPlane.Domain.Repositories;

public interface IAccessRequestRepository
{
    Task<AccessRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<AccessRequest>> FindAsync(FilterDefinition<AccessRequest> filter, int limit = 100, CancellationToken ct = default);
    Task InsertAsync(AccessRequest request, CancellationToken ct = default);
    Task ReplaceAsync(AccessRequest request, CancellationToken ct = default);

    /// <summary>Bulk-update status for expired requests. Returns count of updated documents.</summary>
    Task<long> ExpirePendingAsync(DateTimeOffset cutoff, CancellationToken ct = default);

    /// <summary>Bulk-update status for expired grants. Returns count of updated documents.</summary>
    Task<long> ExpireGrantsAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
