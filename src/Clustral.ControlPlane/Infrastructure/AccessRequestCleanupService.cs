using Clustral.ControlPlane.Domain;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure;

/// <summary>
/// Background service that periodically expires pending access requests
/// whose TTL has elapsed.
/// </summary>
public sealed class AccessRequestCleanupService(
    ClustralDb db,
    ILogger<AccessRequestCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                // Expire pending requests past their TTL.
                var expireResult = await db.AccessRequests.UpdateManyAsync(
                    r => r.Status == AccessRequestStatus.Pending && r.RequestExpiresAt <= now,
                    Builders<AccessRequest>.Update.Set(r => r.Status, AccessRequestStatus.Expired),
                    cancellationToken: stoppingToken);

                if (expireResult.ModifiedCount > 0)
                    logger.LogInformation("Expired {Count} pending access request(s)", expireResult.ModifiedCount);

                // Expire approved grants past their natural expiry (not revoked).
                var grantExpireResult = await db.AccessRequests.UpdateManyAsync(
                    r => r.Status == AccessRequestStatus.Approved
                      && r.GrantExpiresAt <= now
                      && r.RevokedAt == null,
                    Builders<AccessRequest>.Update.Set(r => r.Status, AccessRequestStatus.Expired),
                    cancellationToken: stoppingToken);

                if (grantExpireResult.ModifiedCount > 0)
                    logger.LogInformation("Expired {Count} approved grant(s) past their TTL", grantExpireResult.ModifiedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during access request cleanup");
            }
        }
    }
}
