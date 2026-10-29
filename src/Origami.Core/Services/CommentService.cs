namespace Origami.Core.Services;

/// <summary>
/// 訪客評論：張貼預設待審（可設定自動核可），核可後才公開；蜜罐與連結數
/// 過量者靜默標為垃圾。基礎反垃圾不依賴任何第三方雲端服務。
/// </summary>
public sealed partial class CommentService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider clock)
{
    public sealed record CommentPostInput(string AuthorName, string? Url, string Markdown, bool Honeypot);
    public sealed record CommentView(Guid Id, Guid ContentId, string ContentTitle, string AuthorName, string Url,
        string Markdown, CommentStatus Status, DateTimeOffset CreatedAt, Guid Version);
    public sealed record CommentCounts(int Pending, int Approved, int Spam, int Trashed);

    /// <summary>
    /// 訪客張貼評論。僅限已發布、未受保護且非私密的文章；成功一律回傳待審語意，
    /// 蜜罐命中者靜默進入垃圾，不對呼叫端透露分類結果。
    /// </summary>
    public async Task PostAsync(Guid contentId, CommentPostInput input, CancellationToken ct = default)
    {
        var name = (input.AuthorName ?? "").Trim();
        if (name.Length is 0 or > Limits.CommentAuthor || name.Any(char.IsControl))
            throw new CmsException($"名稱須為 1–{Limits.CommentAuthor} 字。");
        var body = (input.Markdown ?? "").Trim();
        if (body.Length is 0 or > Limits.CommentBody)
            throw new CmsException($"評論內容須為 1–{Limits.CommentBody} 字。");
        var url = (input.Url ?? "").Trim();
        if (url.Length > Limits.CommentUrl) throw new CmsException($"網址最長 {Limits.CommentUrl} 字。");
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new CmsException("網址必須是 http 或 https 的絕對網址。");

        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
        if (!settings.CommentsEnabled) throw new CmsException("本站未開放評論。");
        var content = await db.Contents.AsNoTracking().Where(x => x.Id == contentId)
            .Select(x => new { x.Kind, x.Status, x.IsProtected, x.IsPrivate }).SingleOrDefaultAsync(ct)
            ?? throw new CmsException("內容不存在。");
        if (content.Kind != ContentKind.Post || content.Status != ContentStatus.Published
            || content.IsProtected || content.IsPrivate)
            throw new CmsException("這篇內容不接受評論。");

        var status = settings.CommentAutoApprove ? CommentStatus.Approved : CommentStatus.Pending;
        // 蜜罐欄位有值＝填表的是機器人；連結過量是常見垃圾特徵。兩者都靜默標為垃圾。
        if (input.Honeypot || CountLinks(body) > Limits.CommentMaxLinks) status = CommentStatus.Spam;

        db.Comments.Add(new ContentComment
        {
            SiteId = db.CurrentSiteId, ContentId = contentId,
            AuthorName = name, Url = url, Markdown = body,
            Status = status, CreatedAt = clock.GetUtcNow()
        });
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>已核可的公開評論（閱讀順序：舊到新）；每次讀取重驗內容可評性與本站開關。</summary>
    public async Task<List<CommentView>> ListPublicAsync(Guid contentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await PublicComments(db, contentId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new CommentView(x.Id, x.ContentId, x.Content.Title, x.AuthorName, x.Url, x.Markdown, x.Status, x.CreatedAt, x.Version))
            .Take(Limits.CommentsPageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountApprovedAsync(Guid contentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await PublicComments(db, contentId).CountAsync(ct);
    }

    // 可見性與評論在同一個資料庫查詢中判斷；三個實體均沿用目前站點的查詢篩選器。
    private static IQueryable<ContentComment> PublicComments(CmsDbContext db, Guid contentId) =>
        db.Comments.AsNoTracking().Where(x => x.ContentId == contentId && x.Status == CommentStatus.Approved
            && x.Content.Kind == ContentKind.Post && x.Content.Status == ContentStatus.Published
            && !x.Content.IsProtected && !x.Content.IsPrivate
            && db.Settings.Any(settings => settings.Id == 1 && settings.CommentsEnabled));

    public async Task<PagedResult<CommentView>> ListAdminAsync(CommentStatus? status, int page = 1,
        int pageSize = Limits.PageSize, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        if (status is { } filter && !Enum.IsDefined(filter)) throw new CmsException("評論狀態無效。");
        var offset = ContentRules.Offset(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Comments.AsNoTracking() as IQueryable<ContentComment>;
        if (status is not null) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize)
            .Select(x => new CommentView(x.Id, x.ContentId, x.Content.Title, x.AuthorName, x.Url, x.Markdown, x.Status, x.CreatedAt, x.Version))
            .ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<CommentCounts> CountAdminAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var counts = await db.Comments.AsNoTracking()
            .GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
        return new(
            counts.Where(x => x.Status == CommentStatus.Pending).Sum(x => x.Count),
            counts.Where(x => x.Status == CommentStatus.Approved).Sum(x => x.Count),
            counts.Where(x => x.Status == CommentStatus.Spam).Sum(x => x.Count),
            counts.Where(x => x.Status == CommentStatus.Trashed).Sum(x => x.Count));
    }

    /// <summary>審核：核可／退回待審／標記垃圾／移入垃圾桶（可再救回）。並發版本不符合時拒絕，不靜默覆寫。</summary>
    public async Task ModerateAsync(Guid id, CommentStatus target, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (!Enum.IsDefined(target)) throw new CmsException("評論狀態無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var comment = await db.Comments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("評論不存在。");
        if (comment.Version != version) throw new CmsException(StaleMessage);
        comment.Status = target;
        comment.Version = Guid.NewGuid();
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>永久刪除；由後台人工確認後執行，同樣做並發版本檢查。</summary>
    public async Task DeleteAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var comment = await db.Comments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("評論不存在。");
        if (comment.Version != version) throw new CmsException(StaleMessage);
        db.Comments.Remove(comment);
        await ContentRules.SaveAsync(db, ct);
    }

    private const string StaleMessage = "這則評論已被另一個分頁處理，請重新載入後再試。";

    [GeneratedRegex(@"https?://", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    private static int CountLinks(string body) => LinkPattern().Matches(body).Count;
}
