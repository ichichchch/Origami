namespace Origami.Core.Services;

/// <summary>可信 BFF 驗證外部身分後的本人綁定入口；不建立或依電子郵件合併帳號。</summary>
public sealed class ExternalLoginService(
    CmsDbContext db,
    AdminAccess admin,
    UserManager<AdminUser> userManager,
    TimeProvider timeProvider)
{
    private const int MaximumProviderKeyLength = 256;

    public async Task<IReadOnlyList<ExternalLoginSummary>> ListAsync(CancellationToken ct = default)
    {
        var user = await admin.RequireNonCredentialUserAsync(ct);
        // 投影只取供應商，絕不把外部帳號識別或資料庫中未驗證的顯示名稱送回網站。
        var providers = await db.UserLogins.AsNoTracking().Where(x => x.UserId == user.Id &&
                (x.LoginProvider == "Google" || x.LoginProvider == "Microsoft" ||
                 x.LoginProvider == "GitHub" || x.LoginProvider == "WeChat"))
            .Select(x => x.LoginProvider).Distinct().OrderBy(x => x).ToListAsync(ct);
        return providers.Select(provider => new ExternalLoginSummary(provider, DisplayName(provider))).ToArray();
    }

    public async Task LinkAsync(ExternalLoginLinkInput input, CancellationToken ct = default)
    {
        var user = await admin.RequireNonCredentialUserAsync(ct);
        ArgumentNullException.ThrowIfNull(input);
        _ = DisplayName(input.LoginProvider);
        if (string.IsNullOrEmpty(input.ProviderKey) || input.ProviderKey.Length > MaximumProviderKeyLength ||
            input.ProviderKey.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            throw new CmsException("外部帳號識別須為 1–256 字，且不得含空白或控制字元。");
        await ChangeAsync(user, input.LoginProvider, input.ProviderKey, ct);
    }

    public async Task UnlinkAsync(string provider, CancellationToken ct = default)
    {
        var user = await admin.RequireNonCredentialUserAsync(ct);
        _ = DisplayName(provider);
        await ChangeAsync(user, provider, null, ct);
    }

    private async Task ChangeAsync(AdminUser sessionUser, string provider, string? providerKey, CancellationToken ct)
    {
        await SiteAdministrationService.WriteSafelyAsync(async () =>
        {
            // 注入的 request-scoped context 與 UserManager 的 EF store 是同一個實例。
            // 不使用 factory 另開交易，否則 Identity 的自動儲存不會被一起回滾。
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // 與停用／抹除／憑證操作維持先站點、再使用者的鎖序，審計也會參照站點。
            await SiteAdministrationService.LockSiteAsync(db, db.CurrentSiteId, ct);
            var user = await db.Users
                .FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {sessionUser.Id} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw InvalidSession();
            // Store 可能已追蹤使用者；在列鎖內重新讀取，不能使用追蹤快取中的舊戳記。
            await db.Entry(user).ReloadAsync(ct);
            var currentSession = await admin.RequireNonCredentialUserAsync(ct);
            if (user.IsDisabled || string.IsNullOrEmpty(user.SecurityStamp) ||
                user.Id != currentSession.Id ||
                !string.Equals(user.SecurityStamp, sessionUser.SecurityStamp, StringComparison.Ordinal) ||
                !string.Equals(user.SecurityStamp, currentSession.SecurityStamp, StringComparison.Ordinal))
                throw InvalidSession();

            AuditLog? audit = null;
            var committed = false;
            try
            {
                var keys = await db.UserLogins.AsNoTracking()
                    .Where(x => x.UserId == user.Id && x.LoginProvider == provider)
                    .Select(x => x.ProviderKey).Take(2).ToListAsync(ct);
                IdentityResult result;
                ct.ThrowIfCancellationRequested();
                if (providerKey is not null)
                {
                    if (keys.Count != 0)
                        throw new CmsException("此供應商已綁定，請先解除綁定再更換帳號。");
                    // provider/key 主鍵是跨帳號競爭的最後防線；同帳號則由使用者列鎖序列化。
                    result = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, DisplayName(provider)));
                }
                else
                {
                    if (keys.Count != 1)
                        throw new CmsException("綁定不存在或狀態不一致，請重新載入後再試。");
                    result = await userManager.RemoveLoginAsync(user, provider, keys[0]);
                }
                EnsureSucceeded(result);
                ct.ThrowIfCancellationRequested();
                EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
                audit = new AuditLog
                {
                    SiteId = db.CurrentSiteId,
                    UserId = user.Id,
                    UserName = user.UserName ?? "",
                    Action = providerKey is not null ? "user.external_login_linked" : "user.external_login_unlinked",
                    Target = provider,
                    CreatedAt = timeProvider.GetUtcNow()
                };
                db.AuditLogs.Add(audit);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                committed = true;
                return true;
            }
            finally
            {
                if (!committed)
                {
                    // 交易釋放時回滾；同一 scope 若捕捉錯誤後繼續使用，不得再儲存本次失敗的追蹤狀態。
                    if (audit is not null) db.Entry(audit).State = EntityState.Detached;
                    foreach (var entry in db.ChangeTracker.Entries<IdentityUserLogin<string>>()
                                 .Where(x => x.Entity.UserId == user.Id && x.Entity.LoginProvider == provider).ToArray())
                        entry.State = EntityState.Detached;
                    db.Entry(user).State = EntityState.Detached;
                }
            }
        });
    }

    private static string DisplayName(string provider) => provider switch
    {
        "Google" => "Google",
        "Microsoft" => "Microsoft",
        "GitHub" => "GitHub",
        "WeChat" => "微信",
        _ => throw new CmsException("不支援此外部登入供應商。")
    };

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new CmsException("無法變更外部登入綁定，請重新登入後再試。");
    }

    private static UnauthorizedAccessException InvalidSession() => new("登入已失效，請重新登入。");
}
