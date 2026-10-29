namespace Origami.Core.Services;

public sealed class SettingsService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider? clock = null)
{
    public async Task<SiteSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
    }

    public async Task<SiteSettings> SaveAsync(SiteSettings input, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var access = await admin.GetCurrentAccessAsync(ct);
        ContentRules.Validate(input);
        if (input.Id != 1) throw new CmsException("站點設定識別無效。");
        if (input.PostsPerPage is { } posts && (posts < 1 || posts > Limits.MaxPageSize))
            throw new CmsException($"每頁文章數須為 1–{Limits.MaxPageSize}，或留空使用預設值。");
        if (input.FeedItems is { } feed && (feed < 1 || feed > Limits.MaxFeedItems))
            throw new CmsException($"Feed 條數上限為 {Limits.MaxFeedItems}，或留空使用預設值。");
        // 受控版型：預設必須來自內建清單，色碼與欄寬皆須通過檢查，避免注入樣式。
        var preset = ThemePresets.Find(input.ThemePreset) ?? throw new CmsException("版型預設無效。");
        if (!ThemePresets.IsValidColor(input.AccentColor)) throw new CmsException("版型色碼須為 # 加三或六位十六進位數字。");
        if (!ThemePresets.IsValidReadingWidth(input.ReadingWidth)) throw new CmsException("正文欄寬須為 48–96 字。");
        if (input.CardStyle is not (ThemePresets.CardStyleList or ThemePresets.CardStyleGrid))
            throw new CmsException("卡片樣式無效。");
        var colorScheme = string.IsNullOrWhiteSpace(input.ColorScheme) ? ThemePresets.ColorSchemeAuto : input.ColorScheme.Trim().ToLowerInvariant();
        if (!ThemePresets.IsValidColorScheme(colorScheme))
            throw new CmsException("色彩模式無效（僅接受 auto、light 或 dark）。");
        ValidateOptionalHttpUrl(input.SocialGithubUrl, "GitHub 連結");
        ValidateOptionalHttpUrl(input.SocialXUrl, "X / Twitter 連結");
        ValidateOptionalHttpUrl(input.SocialMastodonUrl, "Mastodon / 社群連結");
        if (!WebhookService.IsAllowedWebhookUrl(input.WebhookUrl))
            throw new CmsException("Webhook 網址必須是公開 https 絕對網址（不允許本機或內網位址）。");
        if (input.CommentCloseDays is < 0 or > 3650)
            throw new CmsException("自動關閉評論天數須為 0–3650 天（0 表示不自動關閉）。");
        var permalinkScheme = string.IsNullOrWhiteSpace(input.PermalinkScheme) ? PermalinkRules.PostName : input.PermalinkScheme.Trim().ToLowerInvariant();
        if (!PermalinkRules.IsValidScheme(permalinkScheme))
            throw new CmsException("永久連結結構方案無效。");
        var timeZoneId = string.IsNullOrWhiteSpace(input.TimeZoneId) ? "UTC" : input.TimeZoneId.Trim();
        if (!PermalinkRules.IsValidTimeZone(timeZoneId))
            throw new CmsException("時區識別無效，請選擇有效的系統時區或 UTC。");
        var dateFormat = string.IsNullOrWhiteSpace(input.DateFormat) ? "yyyy.MM.dd" : input.DateFormat.Trim();
        if (!PermalinkRules.IsValidDateFormat(dateFormat))
            throw new CmsException("日期格式無效。");
        var timeFormat = string.IsNullOrWhiteSpace(input.TimeFormat) ? "HH:mm" : input.TimeFormat.Trim();
        if (!PermalinkRules.IsValidTimeFormat(timeFormat))
            throw new CmsException("時間格式無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (input.HomePageContentId is { } homeId)
        {
            var home = await db.Contents.AsNoTracking().Where(x => x.Id == homeId)
                .Select(x => new { x.Kind, x.Status, x.IsPrivate, x.IsProtected }).SingleOrDefaultAsync(ct);
            if (home is null || home.Kind != ContentKind.Page || home.Status != ContentStatus.Published || home.IsPrivate || home.IsProtected)
                throw new CmsException("首頁固定頁面必須是已發布、未受保護且非私密的獨立頁面。");
        }
        if (input.PrivacyPageContentId is { } privacyId)
        {
            var privacy = await db.Contents.AsNoTracking().Where(x => x.Id == privacyId)
                .Select(x => new { x.Kind, x.Status, x.IsPrivate, x.IsProtected }).SingleOrDefaultAsync(ct);
            if (privacy is null || privacy.Kind != ContentKind.Page || privacy.Status != ContentStatus.Published || privacy.IsPrivate || privacy.IsProtected)
                throw new CmsException("隱私權政策頁面必須是已發布、未受保護且非私密的獨立頁面。");
        }
        if (input.SiteIconMediaId is { } iconId && !await db.MediaAssets.AsNoTracking().AnyAsync(x => x.Id == iconId, ct))
            throw new CmsException("網站圖示不存在，請重新選擇。");
        if (input.LogoMediaId is { } logoId && !await db.MediaAssets.AsNoTracking().AnyAsync(x => x.Id == logoId, ct))
            throw new CmsException("網站標誌不存在，請重新選擇。");
        var entity = await db.Settings.SingleAsync(x => x.Id == 1, ct);
        db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        entity.Name = input.Name.Trim();
        entity.Description = input.Description?.Trim() ?? "";
        entity.PostsPerPage = input.PostsPerPage;
        entity.FeedItems = input.FeedItems;
        entity.FeedShowFullText = input.FeedShowFullText;
        entity.AuthorDisplayName = (input.AuthorDisplayName ?? "").Trim();
        entity.HomePageContentId = input.HomePageContentId;
        entity.PrivacyPageContentId = input.PrivacyPageContentId;
        entity.EmailNotificationsEnabled = input.EmailNotificationsEnabled;
        entity.PermalinkScheme = permalinkScheme;
        entity.TimeZoneId = timeZoneId;
        entity.DateFormat = dateFormat;
        entity.TimeFormat = timeFormat;
        entity.SiteIconMediaId = input.SiteIconMediaId;
        entity.LogoMediaId = input.LogoMediaId;
        entity.CustomNavigationEnabled = input.CustomNavigationEnabled;
        entity.CommentsEnabled = input.CommentsEnabled;
        entity.CommentAutoApprove = input.CommentAutoApprove;
        entity.CommentBlockedWords = (input.CommentBlockedWords ?? "").Trim();
        entity.CommentCloseDays = input.CommentCloseDays;
        entity.ThemePreset = preset.Name;
        entity.AccentColor = input.AccentColor.Trim().ToLowerInvariant();
        entity.ReadingWidth = input.ReadingWidth;
        entity.ShowByline = input.ShowByline;
        entity.ShowCover = input.ShowCover;
        entity.CardStyle = input.CardStyle;
        entity.ColorScheme = colorScheme;
        entity.FooterText = (input.FooterText ?? "").Trim();
        entity.SocialGithubUrl = (input.SocialGithubUrl ?? "").Trim();
        entity.SocialXUrl = (input.SocialXUrl ?? "").Trim();
        entity.SocialMastodonUrl = (input.SocialMastodonUrl ?? "").Trim();
        entity.MaintenanceMode = input.MaintenanceMode;
        entity.MaintenanceMessage = (input.MaintenanceMessage ?? "").Trim();
        entity.WebhookUrl = (input.WebhookUrl ?? "").Trim();
        entity.WebhookSecret = (input.WebhookSecret ?? "").Trim();
        entity.Version = Guid.NewGuid();
        db.AuditLogs.Add(new AuditLog
        {
            SiteId = db.CurrentSiteId,
            UserId = access.UserId ?? "",
            UserName = access.UserName ?? "",
            Action = "settings.updated",
            Target = entity.Name + (entity.MaintenanceMode ? " (維護模式開啟)" : ""),
            CreatedAt = (clock ?? TimeProvider.System).GetUtcNow()
        });
        await ContentRules.SaveAsync(db, ct);
        input.ColorScheme = entity.ColorScheme;
        input.PermalinkScheme = entity.PermalinkScheme;
        input.TimeZoneId = entity.TimeZoneId;
        input.DateFormat = entity.DateFormat;
        input.TimeFormat = entity.TimeFormat;
        input.Version = entity.Version;
        return input;
    }

    private static void ValidateOptionalHttpUrl(string? url, string label)
    {
        var clean = (url ?? "").Trim();
        if (clean.Length == 0) return;
        if (clean.Length > Limits.SocialUrl || !Uri.TryCreate(clean, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new CmsException($"{label}必須是 http 或 https 的絕對網址。");
    }
}
