namespace Origami.Web.Services;

/// <summary>Liveness backed by a real database round-trip; no provider or configuration details are exposed.</summary>
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
