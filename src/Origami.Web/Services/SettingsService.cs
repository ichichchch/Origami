namespace Origami.Web.Services;

public sealed class SettingsService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin)
{
    public async Task<SiteSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
    }

    public async Task<SiteSettings> SaveAsync(SiteSettings input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ContentRules.Validate(input);
        if (input.Id != 1) throw new CmsException("站點設定識別無效。");
        if (input.PostsPerPage is { } posts && (posts < 1 || posts > Limits.MaxPageSize))
            throw new CmsException($"每頁文章數須為 1–{Limits.MaxPageSize}，或留空使用預設值。");
        if (input.FeedItems is { } feed && (feed < 1 || feed > Limits.MaxFeedItems))
            throw new CmsException($"Feed 條數上限為 {Limits.MaxFeedItems}，或留空使用預設值。");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (input.HomePageContentId is { } homeId)
        {
            var home = await db.Contents.AsNoTracking().Where(x => x.Id == homeId)
                .Select(x => new { x.Kind, x.Status, x.IsPrivate, x.IsProtected }).SingleOrDefaultAsync(ct);
            if (home is null || home.Kind != ContentKind.Page || home.Status != ContentStatus.Published || home.IsPrivate || home.IsProtected)
                throw new CmsException("首頁固定頁面必須是已發布、未受保護且非私密的獨立頁面。");
        }
        if (input.SiteIconMediaId is { } iconId && !await db.MediaAssets.AsNoTracking().AnyAsync(x => x.Id == iconId, ct))
            throw new CmsException("網站圖示不存在，請重新選擇。");
        var entity = await db.Settings.SingleAsync(x => x.Id == 1, ct);
        db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        entity.Name = input.Name.Trim();
        entity.Description = input.Description?.Trim() ?? "";
        entity.PostsPerPage = input.PostsPerPage;
        entity.FeedItems = input.FeedItems;
        entity.HomePageContentId = input.HomePageContentId;
        entity.SiteIconMediaId = input.SiteIconMediaId;
        entity.Version = Guid.NewGuid();
        await ContentRules.SaveAsync(db, ct);
        input.Version = entity.Version;
        return input;
    }
}
