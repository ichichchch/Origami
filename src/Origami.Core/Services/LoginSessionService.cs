namespace Origami.Core.Services;

/// <summary>登入工作階段管理：裝置清單與單一會話撤銷；「登出」仍以安全戳記輪替撤銷全部會話。</summary>
public sealed class LoginSessionService(IDbContextFactory<CmsDbContext> factory, TimeProvider? clock = null)
{
    public sealed record SessionView(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
        string UserAgent, string RemoteIp, string Provider);

    /// <summary>某使用者目前仍有效的會話（新到舊）；已撤銷與已到期不出列。</summary>
    public async Task<List<SessionView>> ListAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) throw new CmsException("使用者識別無效。");
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LoginSessions.AsNoTracking()
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new SessionView(x.Id, x.CreatedAt, x.ExpiresAt, x.UserAgent, x.RemoteIp, x.Provider))
            .ToListAsync(ct);
    }

    /// <summary>撤銷單一會話（僅限本人的會話；重複撤銷視為成功）。該會話的下一個請求即失效。</summary>
    public async Task RevokeAsync(string userId, Guid sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) throw new CmsException("使用者識別無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var session = await db.LoginSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);
        if (session is null) throw new CmsException("找不到這個登入工作階段。");
        if (session.RevokedAt is null)
        {
            session.RevokedAt = (clock ?? TimeProvider.System).GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>撤銷某使用者全部會話；搭配安全戳記輪替（登出、改密碼、兩步驟驗證變更）同步清理資料列。</summary>
    public static async Task RevokeAllAsync(IDbContextFactory<CmsDbContext> factory, string userId,
        TimeProvider? clock = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return;
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.LoginSessions.Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now), ct);
    }
}
