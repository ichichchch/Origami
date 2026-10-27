namespace Origami.Core.Services;

/// <summary>以真實資料庫往返支撐的活躍度檢查；不揭露供應商或設定細節。</summary>
public sealed class CmsHealthCheck(IDbContextFactory<CmsDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return HealthCheckResult.Unhealthy(); }
    }
}
