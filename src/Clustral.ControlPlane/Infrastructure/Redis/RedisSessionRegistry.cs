using StackExchange.Redis;

namespace Clustral.ControlPlane.Infrastructure.Redis;

/// <summary>
/// Looks up which TunnelService pod holds the active tunnel session
/// for a given cluster by querying Redis.
/// </summary>
public interface IRedisSessionRegistry
{
    Task<string?> LookupSessionAsync(Guid clusterId, CancellationToken ct = default);
}

public sealed class RedisSessionRegistry : IRedisSessionRegistry
{
    private readonly IConnectionMultiplexer _redis;

    public RedisSessionRegistry(IConfiguration config)
    {
        _redis = ConnectionMultiplexer.Connect(
            config["Redis:ConnectionString"] ?? "localhost:6379");
    }

    public async Task<string?> LookupSessionAsync(Guid clusterId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync($"tunnel:session:{clusterId}");
        return value.HasValue ? value.ToString() : null;
    }
}
