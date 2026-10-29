namespace Origami.Core.Services;

/// <summary>
/// 訪客評論：張貼預設待審（可設定自動核可），核可後才公開；蜜罐與連結數
/// 過量者靜默標為垃圾。基礎反垃圾不依賴任何第三方雲端服務。
/// </summary>
public sealed partial class CommentService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider clock, WebhookService? webhooks = null, IEmailSender? emailSender = null)
{
    public sealed record CommentPostInput(string AuthorName, string? Url, string Markdown, bool Honeypot, Guid? ParentId = null);
    public sealed record CommentView(Guid Id, Guid ContentId, string ContentTitle, string AuthorName, string Url,
        string Markdown, CommentStatus Status, DateTimeOffset CreatedAt, Guid Version,
        Guid? ParentId = null, bool IsAdminReply = false, string ParentAuthorName = "");
    public sealed record CommentCounts(int Pending, int Approved, int Spam, int Trashed);

    /// <summary>
    /// 訪客張貼評論。僅限已發布、未受保護、非私密且允許評論的文章；成功一律回傳待審語意，
    /// 蜜罐與敏感詞命中者靜默進入垃圾，不對呼叫端透露分類結果。
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
            .Select(x => new { x.Kind, x.Status, x.IsProtected, x.IsPrivate, x.AllowComments, x.PublishedAt, x.Title, x.Slug, x.AuthorId })
            .SingleOrDefaultAsync(ct)
            ?? throw new CmsException("內容不存在。");
        if (content.Kind != ContentKind.Post || content.Status != ContentStatus.Published
            || content.IsProtected || content.IsPrivate || !content.AllowComments)
            throw new CmsException("這篇內容不接受評論。");
        var now = clock.GetUtcNow();
        if (settings.CommentCloseDays > 0 && content.PublishedAt is { } publishedAt
            && publishedAt.AddDays(settings.CommentCloseDays) < now)
            throw new CmsException("這篇文章發布已超過評論開放期限，目前已關閉評論。");
        if (input.ParentId is { } parentId)
        {
            var parentOk = await db.Comments.AsNoTracking()
                .AnyAsync(x => x.Id == parentId && x.ContentId == contentId && x.Status == CommentStatus.Approved, ct);
            if (!parentOk) throw new CmsException("回覆的評論不存在或尚未核可。");
        }

        var status = settings.CommentAutoApprove ? CommentStatus.Approved : CommentStatus.Pending;
        // 蜜罐欄位有值＝填表的是機器人；連結過量或命中站點敏感詞黑名單者靜默標為垃圾。
        if (input.Honeypot || CountLinks(body) > Limits.CommentMaxLinks || ContainsBlockedWords(settings.CommentBlockedWords, name, body, url))
            status = CommentStatus.Spam;

        var entity = new ContentComment
        {
            SiteId = db.CurrentSiteId, ContentId = contentId, ParentId = input.ParentId,
            AuthorName = name, Url = url, Markdown = body,
            Status = status, CreatedAt = now
        };
        db.Comments.Add(entity);
        await ContentRules.SaveAsync(db, ct);
        if (status != CommentStatus.Spam && webhooks is not null)
        {
            await webhooks.DispatchAsync("comment.created", new
            {
                id = entity.Id,
                contentId,
                contentTitle = content.Title,
                contentSlug = content.Slug,
                authorName = name,
                status = status.ToString().ToLowerInvariant()
            }, ct);
        }
        if (status != CommentStatus.Spam && settings.EmailNotificationsEnabled && emailSender is { IsConfigured: true })
        {
            await NotifyNewCommentEmailAsync(db, settings.Name, content.Title, content.Slug, content.AuthorId, name, body, status, ct);
        }
    }

    private async Task NotifyNewCommentEmailAsync(CmsDbContext db, string siteName, string contentTitle, string contentSlug,
        string? authorId, string commenterName, string markdown, CommentStatus status, CancellationToken ct)
    {
        try
        {
            var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(authorId))
            {
                var authorEmail = await db.Users.AsNoTracking()
                    .Where(x => x.Id == authorId && !x.IsDisabled && x.Email != null && x.Email != "")
                    .Select(x => x.Email!)
                    .SingleOrDefaultAsync(ct);
                if (!string.IsNullOrWhiteSpace(authorEmail)) recipients.Add(authorEmail);
            }
            var editorEmails = await db.SiteMemberships.AsNoTracking()
                .Where(x => !x.User.IsDisabled && x.Role >= SiteRole.Editor && x.User.Email != null && x.User.Email != "")
                .Select(x => x.User.Email!)
                .ToListAsync(ct);
            foreach (var email in editorEmails) recipients.Add(email);
            var netAdminEmails = await db.Users.AsNoTracking()
                .Where(x => x.SingletonKey == 1 && !x.IsDisabled && x.Email != null && x.Email != "")
                .Select(x => x.Email!)
                .ToListAsync(ct);
            foreach (var email in netAdminEmails) recipients.Add(email);

            var statusLabel = status == CommentStatus.Pending ? "待審核" : "已自動核可";
            foreach (var to in recipients)
            {
                await emailSender!.SendAsync(to,
                    $"[{siteName}] 新評論通知（{statusLabel}）：{contentTitle}",
                    $"訪客「{commenterName}」在文章「{contentTitle}」（/posts/{contentSlug}）留下了評論（狀態：{statusLabel}）：\n\n{markdown}",
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 郵件通知採盡力而為。
        }
    }

    /// <summary>全站已核可評論訂閱資料（最新在前，僅限公開可讀且允許評論的文章）。</summary>
    public async Task<List<CommentFeedEntry>> FeedCommentsAsync(int count, CancellationToken ct = default)
    {
        if (count is < 1 or > Limits.MaxFeedItems) throw new CmsException("訂閱筆數超出範圍。");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Settings.AsNoTracking().AnyAsync(s => s.Id == 1 && s.CommentsEnabled, ct))
            return [];
        return await db.Comments.AsNoTracking()
            .Where(x => x.Status == CommentStatus.Approved
                && x.Content.Kind == ContentKind.Post && x.Content.Status == ContentStatus.Published
                && !x.Content.IsProtected && !x.Content.IsPrivate && x.Content.AllowComments
                && (x.ParentId == null || x.Parent!.Status == CommentStatus.Approved))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(count)
            .Select(x => new CommentFeedEntry(
                x.Id,
                x.ContentId,
                x.Content.Title,
                x.Content.Slug,
                x.Content.ContentType != null
                    ? "/" + ContentTypeService.PublicPrefix + "/" + x.Content.ContentType.Slug + "/" + x.Content.Slug
                    : "/posts/" + x.Content.Slug,
                x.AuthorName,
                x.Markdown,
                x.CreatedAt))
            .ToListAsync(ct);
    }

    private static bool ContainsBlockedWords(string? blockedWordsRaw, string authorName, string markdown, string url)
    {
        if (string.IsNullOrWhiteSpace(blockedWordsRaw)) return false;
        var words = blockedWordsRaw.Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            if (authorName.Contains(word, StringComparison.OrdinalIgnoreCase)
                || markdown.Contains(word, StringComparison.OrdinalIgnoreCase)
                || url.Contains(word, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>後台管理員／編輯者直接回覆評論；回覆自動為已核可狀態，若原評論尚待審則一併核可。</summary>
    public async Task<CommentView> ReplyAdminAsync(Guid parentCommentId, string markdown, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var access = await admin.GetCurrentAccessAsync(ct);
        var body = (markdown ?? "").Trim();
        if (body.Length is 0 or > Limits.CommentBody)
            throw new CmsException($"回覆內容須為 1–{Limits.CommentBody} 字。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var parent = await db.Comments.Include(x => x.Content).SingleOrDefaultAsync(x => x.Id == parentCommentId, ct)
            ?? throw new CmsException("要回覆的評論不存在。");
        if (parent.Status == CommentStatus.Pending)
        {
            parent.Status = CommentStatus.Approved;
            parent.Version = Guid.NewGuid();
        }
        var settings = await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
        var authorName = !string.IsNullOrWhiteSpace(access.DisplayName) ? access.DisplayName
            : !string.IsNullOrWhiteSpace(settings.AuthorDisplayName) ? settings.AuthorDisplayName
            : access.UserName;
        var reply = new ContentComment
        {
            SiteId = db.CurrentSiteId,
            ContentId = parent.ContentId,
            ParentId = parent.Id,
            AuthorName = authorName,
            Url = "",
            Markdown = body,
            IsAdminReply = true,
            Status = CommentStatus.Approved,
            CreatedAt = clock.GetUtcNow()
        };
        db.Comments.Add(reply);
        await ContentRules.SaveAsync(db, ct);
        return new CommentView(reply.Id, reply.ContentId, parent.Content.Title, reply.AuthorName, reply.Url,
            reply.Markdown, reply.Status, reply.CreatedAt, reply.Version, reply.ParentId, reply.IsAdminReply, parent.AuthorName);
    }

    /// <summary>已核可的公開評論（閱讀順序：舊到新）；每次讀取重驗內容可評性與本站開關。</summary>
    public async Task<List<CommentView>> ListPublicAsync(Guid contentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await PublicComments(db, contentId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new CommentView(x.Id, x.ContentId, x.Content.Title, x.AuthorName, x.Url, x.Markdown, x.Status, x.CreatedAt, x.Version,
                x.ParentId, x.IsAdminReply, x.Parent != null ? x.Parent.AuthorName : ""))
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
            && !x.Content.IsProtected && !x.Content.IsPrivate && x.Content.AllowComments
            && (x.ParentId == null || x.Parent!.Status == CommentStatus.Approved)
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
            .Select(x => new CommentView(x.Id, x.ContentId, x.Content.Title, x.AuthorName, x.Url, x.Markdown, x.Status, x.CreatedAt, x.Version,
                x.ParentId, x.IsAdminReply, x.Parent != null ? x.Parent.AuthorName : ""))
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
