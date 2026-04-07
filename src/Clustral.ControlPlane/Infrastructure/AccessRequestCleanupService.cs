using Clustral.ControlPlane.Domain.Repositories;

namespace Clustral.ControlPlane.Infrastructure;

/// <summary>
/// Background service that periodically expires pending access requests
/// whose TTL has elapsed.
/// </summary>
public sealed class AccessRequestCleanupService(
    IServiceScopeFactory scopeFactory,
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
                using var scope = scopeFactory.CreateScope();
                var accessRequests = scope.ServiceProvider.GetRequiredService<IAccessRequestRepository>();

                var now = DateTimeOffset.UtcNow;

                // Expire pending requests past their TTL.
                var expiredCount = await accessRequests.ExpirePendingAsync(now, stoppingToken);

                if (expiredCount > 0)
                    logger.LogInformation("Expired {Count} pending access request(s)", expiredCount);

                // Expire approved grants past their natural expiry (not revoked).
                var grantExpiredCount = await accessRequests.ExpireGrantsAsync(now, stoppingToken);

                if (grantExpiredCount > 0)
                    logger.LogInformation("Expired {Count} approved grant(s) past their TTL", grantExpiredCount);
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
