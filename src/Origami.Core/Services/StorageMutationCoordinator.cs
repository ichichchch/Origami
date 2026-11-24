namespace Origami.Core.Services;

/// <summary>以 PostgreSQL 交易 advisory lock 協調不同主機與請求的站點儲存變更。</summary>
public sealed partial class StorageMutationCoordinator(
    IDbContextFactory<CmsDbContext> factory,
    ILogger<StorageMutationCoordinator> logger)
{
    public async Task<IAsyncDisposable> AcquireAsync(Guid siteId, CancellationToken ct = default)
    {
        if (siteId == Guid.Empty) throw new InvalidOperationException("尚未解析目前站點，無法協調儲存變更。");
        var db = await factory.CreateDbContextAsync(ct);
        IDbContextTransaction? transaction = null;
        var key = BitConverter.ToInt64(siteId.ToByteArray()) ^ 0x4F726947616D6900;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            return new Lease(db, transaction, key, logger);
        }
        catch
        {
            try
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is NpgsqlException or PostgresException or InvalidOperationException)
            {
                if (db.Database.GetDbConnection() is NpgsqlConnection connection)
                    NpgsqlConnection.ClearPool(connection);
                Log.ReleaseFailed(logger, key, ex);
            }
            finally
            {
                await db.DisposeAsync();
            }
            throw;
        }
    }

    private sealed class Lease(
        CmsDbContext db,
        IDbContextTransaction transaction,
        long key,
        ILogger<StorageMutationCoordinator> logger) : IAsyncDisposable
    {
        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is NpgsqlException or PostgresException or InvalidOperationException)
            {
                if (db.Database.GetDbConnection() is NpgsqlConnection connection)
                    NpgsqlConnection.ClearPool(connection);
                Log.ReleaseFailed(logger, key, ex);
            }
            finally
            {
                await db.DisposeAsync();
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Error, "儲存協調鎖 {Key} 無法正常解除；已清除集體連線以強制釋放。")]
        public static partial void ReleaseFailed(ILogger logger, long key, Exception exception);
    }
}
