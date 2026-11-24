namespace Origami.Core.Services;

/// <summary>
/// 帳號相關郵件：歡迎信（含信箱驗證連結）、變更信箱的確認信、密碼變更通知與測試郵件，
/// 以及寄信紀錄與登入歷史的查詢。連結一律帶限時的 Identity 權杖；
/// SMTP 未設定時所有寄信方法都安靜略過，不影響帳號流程。
/// </summary>
public sealed class AccountEmailService(
    UserManager<AdminUser> users,
    IEmailSender mail,
    IDbContextFactory<CmsDbContext> factory,
    TimeProvider? clock = null)
{
    public sealed record Outcome(bool Succeeded, string Message);
    public sealed record EmailLogView(Guid Id, Guid SiteId, string Recipient, string Subject, bool Succeeded, string Error, DateTimeOffset CreatedAt);
    public sealed record LoginEventView(Guid Id, bool Succeeded, string RemoteIp, string UserAgent, DateTimeOffset CreatedAt);

    /// <summary>寄出（或重新寄出）信箱驗證信；帳號無信箱、已驗證或郵件服務未設定時回傳 false。</summary>
    public async Task<bool> SendVerificationAsync(string userId, string origin, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || string.IsNullOrEmpty(user.Email) || user.EmailConfirmed || !mail.IsConfigured) return false;
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = Link(origin, "/account/verify-email", $"user={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}");
        var body = "你好 " + user.UserName + "，\n\n" +
            "你的 Origami 帳號已建立。請開啟以下連結驗證這個信箱，之後才能登入：\n" + link + "\n\n" +
            $"連結在 {PasswordResetService.TokenMinutes} 分鐘內有效；若非本人申請，請忽略此信。";
        return await mail.SendAsync(user.Email, "Origami 帳號：請驗證你的信箱", body, ct);
    }

    /// <summary>完成信箱驗證；權杖無效或過期時回傳明確但不含內部細節的結果。</summary>
    public async Task<Outcome> ConfirmEmailAsync(string userId, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            return new(false, "驗證連結無效或已過期，請重新申請。");
        var user = await users.FindByIdAsync(userId);
        if (user is null || user.IsDisabled) return new(false, "驗證連結無效或已過期，請重新申請。");
        if (user.EmailConfirmed) return new(true, "信箱已驗證。");
        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? new(true, "信箱已驗證，現在可以登入了。") : new(false, "驗證連結無效或已過期，請重新申請。");
    }

    /// <summary>
    /// 申請變更信箱：確認信寄到「新」信箱，點擊連結後才真正變更。
    /// 新信箱已被其他帳號使用時拒絕；與目前信箱相同時不寄信。
    /// </summary>
    public async Task<bool> RequestEmailChangeAsync(string userId, string newEmail, string origin, CancellationToken ct = default)
    {
        var address = (newEmail ?? "").Trim();
        if (address.Length is 0 or > 256 || !address.Contains('@') || address.Any(char.IsControl))
            throw new CmsException("電子郵件格式不正確。");
        var user = await users.FindByIdAsync(userId) ?? throw new CmsException("帳號不存在。");
        if (string.Equals(user.Email, address, StringComparison.OrdinalIgnoreCase)) return false;
        var existing = await users.FindByEmailAsync(address);
        if (existing is not null && existing.Id != user.Id) throw new CmsException("此電子郵件已被其他帳號使用。");
        if (!mail.IsConfigured) throw new CmsException("尚未設定郵件服務，暫時無法變更信箱；請聯絡管理者。");
        var token = await users.GenerateChangeEmailTokenAsync(user, address);
        var link = Link(origin, "/account/confirm-email-change",
            $"user={Uri.EscapeDataString(user.Id)}&email={Uri.EscapeDataString(address)}&token={Uri.EscapeDataString(token)}");
        var body = "你好 " + user.UserName + "，\n\n" +
            "有人要求把這個 Origami 帳號的信箱變更為這個地址。請開啟以下連結確認；確認後新信箱才會生效：\n" + link + "\n\n" +
            $"連結在 {PasswordResetService.TokenMinutes} 分鐘內有效；若非本人操作，請忽略此信，信箱不會變更。";
        return await mail.SendAsync(address, "Origami 帳號：確認變更信箱", body, ct);
    }

    /// <summary>以權杖完成信箱變更；成功後新信箱即為已驗證狀態。</summary>
    public async Task<Outcome> ConfirmEmailChangeAsync(string userId, string email, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
            return new(false, "確認連結無效或已過期，請重新申請。");
        var user = await users.FindByIdAsync(userId);
        if (user is null || user.IsDisabled) return new(false, "確認連結無效或已過期，請重新申請。");
        var result = await users.ChangeEmailAsync(user, email.Trim(), token);
        if (!result.Succeeded) return new(false, "確認連結無效或已過期，或此信箱已被其他帳號使用。");
        // 信箱是由收信人點擊連結確認的，直接標記為已驗證。
        user.EmailConfirmed = true;
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded) return new(false, "信箱已變更，但狀態更新失敗；請重新登入後再試。");
        return new(true, "信箱已變更並完成驗證。");
    }

    /// <summary>密碼變更通知：寄到帳號目前的信箱；未設定信箱或郵件服務時略過。</summary>
    public async Task<bool> NotifyPasswordChangedAsync(string userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || string.IsNullOrEmpty(user.Email) || !mail.IsConfigured) return false;
        var when = (clock ?? TimeProvider.System).GetUtcNow().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return await mail.SendAsync(user.Email, "Origami 帳號：密碼已變更",
            "你好 " + user.UserName + "，\n\n你的 Origami 帳號密碼已於 " + when + " (UTC) 變更，既有登入工作階段已全部登出。\n" +
            "若非本人操作，請立即重設密碼並聯絡管理者。\n", ct);
    }

    /// <summary>寄一封測試郵件，供管理者確認郵件服務設定正確。</summary>
    public async Task<Outcome> SendTestAsync(string to, CancellationToken ct = default)
    {
        var address = (to ?? "").Trim();
        if (address.Length is 0 or > 256 || !address.Contains('@') || address.Any(char.IsControl))
            return new(false, "電子郵件格式不正確。");
        if (!mail.IsConfigured) return new(false, "尚未設定郵件服務（SMTP），無法寄信。");
        var sent = await mail.SendAsync(address, "Origami 測試郵件",
            "這是一封測試郵件：收到此信代表郵件服務設定正確。\n", ct);
        return sent ? new(true, "測試郵件已寄出。") : new(false, "測試郵件寄送失敗；請檢查郵件服務設定。");
    }

    /// <summary>最近的寄信紀錄（新到舊）；只含摘要，不含內文。</summary>
    public async Task<PagedResult<EmailLogView>> ListEmailLogAsync(int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, Limits.MaxPageSize);
        page = Math.Max(1, page);
        await using var db = await factory.CreateDbContextAsync(ct);
        var total = await db.EmailLogs.CountAsync(ct);
        var items = await db.EmailLogs.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new EmailLogView(x.Id, x.SiteId, x.Recipient, x.Subject, x.Succeeded, x.Error, x.CreatedAt))
            .ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    /// <summary>某帳號最近的登入事件（新到舊），最多 50 筆。</summary>
    public async Task<List<LoginEventView>> ListLoginHistoryAsync(string userId, int limit = 20, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LoginEvents.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit)
            .Select(x => new LoginEventView(x.Id, x.Succeeded, x.RemoteIp, x.UserAgent, x.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>記錄一次登入事件（成功或失敗），並為該帳號只保留最新 50 筆。</summary>
    public static async Task RecordLoginAsync(IDbContextFactory<CmsDbContext> factory, string userId, string? userName, bool succeeded,
        string? remoteIp, string? userAgent, DateTimeOffset at, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.LoginEvents.Add(new LoginEvent
        {
            UserId = userId, UserName = Truncate(userName ?? "", 256), Succeeded = succeeded,
            RemoteIp = Truncate(remoteIp ?? "", 64), UserAgent = Truncate(userAgent ?? "", 256), CreatedAt = at
        });
        await db.SaveChangesAsync(ct);
        var stale = await db.LoginEvents.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(50).Select(x => x.Id).ToListAsync(ct);
        if (stale.Count > 0)
            await db.LoginEvents.Where(x => stale.Contains(x.Id)).ExecuteDeleteAsync(ct);
    }

    private static string Link(string origin, string path, string query)
    {
        var baseUri = origin.EndsWith('/') ? origin[..^1] : origin;
        return baseUri + path + "?" + query;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
