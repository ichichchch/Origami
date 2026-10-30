namespace Origami.Core.Services;

/// <summary>
/// 受控小工具：側欄與頁尾的宣告式內容區塊。只有內建型別，組態以 JSON 保存並依型別驗證；
/// 文字小工具的 Markdown 由前台走與內容相同的安全渲染管線。
/// 公開面只讀取可見項目，最新文章與分類標籤皆為即時查詢，撤回後隨即消失。
/// </summary>
public sealed class WidgetService(
    IDbContextFactory<CmsDbContext> factory,
    AdminAccess admin,
    IHttpClientFactory httpClients,
    IMemoryCache cache)
{
    /// <summary>外部 feed 的快取期間；抓取失敗時以較短暫的空清單降級。</summary>
    private static readonly TimeSpan FeedCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FeedFailureTtl = TimeSpan.FromMinutes(1);

    public sealed record WidgetView(Guid Id, WidgetArea Area, WidgetType Type, string Title, int? Count,
        string Markdown, string Url, int Position, bool IsVisible, Guid Version);

    public sealed class WidgetInput
    {
        public Guid? Id { get; set; }
        public WidgetArea Area { get; set; }
        public WidgetType Type { get; set; }
        public string Title { get; set; } = "";
        public int? Count { get; set; }
        public string? Markdown { get; set; }
        public string? Url { get; set; }
        public bool IsVisible { get; set; } = true;
        public Guid Version { get; set; }
    }

    public async Task<List<WidgetView>> ListAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.SiteWidgets.AsNoTracking()
            .OrderBy(x => x.Area).ThenBy(x => x.Position).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Area, x.Type, x.Title, x.ConfigJson, x.Position, x.IsVisible, x.Version })
            .ToListAsync(ct);
        return [.. rows.Select(x =>
        {
            var config = WidgetTypes.ParseConfig(x.ConfigJson);
            return new WidgetView(x.Id, x.Area, x.Type, x.Title, config.Count, config.Markdown ?? "",
                config.Url ?? "", x.Position, x.IsVisible, x.Version);
        })];
    }

    /// <summary>建立或更新小工具；組態依型別驗證，無關欄位一律清空。</summary>
    public async Task<WidgetView> SaveAsync(WidgetInput input, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        if (!Enum.IsDefined(input.Area)) throw new CmsException("小工具區域無效。");
        if (!WidgetTypes.IsValid(input.Type)) throw new CmsException("小工具型別無效。");
        var title = (input.Title ?? "").Trim();
        if (title.Length > Limits.WidgetTitle) throw new CmsException($"小工具標題最長 {Limits.WidgetTitle} 字。");
        var count = input.Count;
        var markdown = (input.Markdown ?? "").Trim();
        var url = (input.Url ?? "").Trim();
        switch (input.Type)
        {
            case WidgetType.RecentPosts:
            case WidgetType.RecentComments:
                count ??= 5;
                if (count is < 1 or > Limits.WidgetRecentPosts)
                    throw new CmsException($"顯示篇數須為 1–{Limits.WidgetRecentPosts}。");
                markdown = "";
                url = "";
                break;
            case WidgetType.RssFeed:
                count ??= 5;
                if (count is < 1 or > Limits.WidgetRecentPosts)
                    throw new CmsException($"Feed 顯示篇數須為 1–{Limits.WidgetRecentPosts}。");
                if (url.Length == 0 || url.Length > Limits.WidgetFeedUrl || !WebhookService.IsAllowedWebhookUrl(url)
                    || Uri.TryCreate(url, UriKind.Absolute, out var feedUri) is false || feedUri.Scheme != Uri.UriSchemeHttps)
                    throw new CmsException("Feed 網址必須是公開 https 絕對網址（不接受本機或內網位址）。");
                markdown = "";
                break;
            case WidgetType.Text:
                count = null;
                url = "";
                if (markdown.Length == 0) throw new CmsException("文字小工具須填入內容。");
                if (markdown.Length > Limits.WidgetMarkdown)
                    throw new CmsException($"文字小工具內容最長 {Limits.WidgetMarkdown} 字。");
                break;
            default:
                count = null;
                markdown = "";
                url = "";
                break;
        }
        await using var db = await factory.CreateDbContextAsync(ct);
        SiteWidget entity;
        if (input.Id is { } existing)
        {
            entity = await db.SiteWidgets.SingleOrDefaultAsync(x => x.Id == existing, ct) ?? throw new CmsException("小工具不存在。");
            // 區域是排序與位置的群組鍵；搬移區域須刪除後重建，不接受就地變更。
            if (entity.Area != input.Area) throw new CmsException("小工具區域不可變更，請刪除後在目標區域重建。");
            if (entity.Version != input.Version) throw new CmsException("資料已被另一個分頁更新，請重新載入後再編輯。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        }
        else
        {
            // 區域內數量有上限；單站單區域的計數查詢成本低，直接以資料庫計數把關。
            var inArea = await db.SiteWidgets.CountAsync(x => x.Area == input.Area, ct);
            if (inArea >= Limits.MaxWidgetsPerArea)
                throw new CmsException($"單一區域最多 {Limits.MaxWidgetsPerArea} 個小工具。");
            entity = new SiteWidget
            {
                Area = input.Area,
                Position = await db.SiteWidgets.AnyAsync(x => x.Area == input.Area, ct)
                    ? await db.SiteWidgets.Where(x => x.Area == input.Area).MaxAsync(x => x.Position, ct) + 1
                    : 0
            };
            db.SiteWidgets.Add(entity);
        }
        entity.Type = input.Type;
        entity.Title = title;
        entity.ConfigJson = WidgetTypes.SerializeConfig(new WidgetTypes.WidgetConfig(count,
            markdown.Length == 0 ? null : markdown, url.Length == 0 ? null : url));
        entity.IsVisible = input.IsVisible;
        entity.Version = Guid.NewGuid();
        await ContentRules.SaveAsync(db, ct);
        return new(entity.Id, entity.Area, entity.Type, entity.Title, count, markdown, url, entity.Position, entity.IsVisible, entity.Version);
    }

    public async Task DeleteAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.SiteWidgets.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("小工具不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        db.SiteWidgets.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>單一區域內依提交順序重新編號；逐筆攜帶版本，單筆失敗不影響其他項目。</summary>
    public async Task<List<BulkItemResult>> ReorderAsync(WidgetArea area, IReadOnlyList<(Guid Id, Guid Version)> items,
        CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        if (!Enum.IsDefined(area)) throw new CmsException("小工具區域無效。");
        if (items.Count == 0) throw new CmsException("請先選擇要排序的項目。");
        if (items.Count > Limits.BulkMaxItems) throw new CmsException($"單次批次操作最多 {Limits.BulkMaxItems} 筆。");
        var results = new List<BulkItemResult>(items.Count);
        await using var db = await factory.CreateDbContextAsync(ct);
        var position = 0;
        foreach (var (id, version) in items)
        {
            var entity = await db.SiteWidgets.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null || entity.Area != area || entity.Version != version)
            {
                results.Add(new BulkItemResult(id, false, "小工具已被其他分頁更新或移動區域，請重新載入。"));
                continue;
            }
            entity.Position = position++;
            entity.Version = Guid.NewGuid();
            results.Add(new BulkItemResult(id, true, ""));
        }
        await ContentRules.SaveAsync(db, ct);
        return results;
    }

    public sealed record PublicWidget(Guid Id, WidgetArea Area, WidgetType Type, string Title, int Count,
        string Markdown, List<WidgetPostLink> RecentPosts, List<WidgetTermLink> Categories, List<WidgetTermLink> Tags,
        List<WidgetCommentLink> RecentComments, List<WidgetRssItem> RssItems);
    public sealed record WidgetPostLink(string Title, string Slug, DateTimeOffset? PublishedAt);
    public sealed record WidgetTermLink(string Name, string Slug, int Depth);
    public sealed record WidgetCommentLink(string AuthorName, string Excerpt, DateTimeOffset CreatedAt,
        string PostTitle, string PostSlug);
    public sealed record WidgetRssItem(string Title, string Url, DateTimeOffset? PublishedAt);

    /// <summary>公開小工具：僅可見項目；最新文章／評論與分類標籤只含已發布、未受保護且非私密的內容。</summary>
    public async Task<List<PublicWidget>> PublicAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var widgets = await db.SiteWidgets.AsNoTracking()
            .Where(x => x.IsVisible)
            .OrderBy(x => x.Area).ThenBy(x => x.Position).ThenBy(x => x.Id)
            .ToListAsync(ct);

        var recentMax = widgets.Where(x => x.Type == WidgetType.RecentPosts)
            .Select(x => WidgetTypes.ParseConfig(x.ConfigJson).Count ?? 5).DefaultIfEmpty(5).Max();
        List<WidgetPostLink> recent = [];
        if (widgets.Any(x => x.Type == WidgetType.RecentPosts))
        {
            recent = await db.Contents.AsNoTracking()
                .Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published && !x.IsPrivate && !x.IsProtected)
                .OrderByDescending(x => x.PublishedAt).ThenBy(x => x.Id)
                .Take(recentMax)
                .Select(x => new WidgetPostLink(x.Title, x.Slug, x.PublishedAt))
                .ToListAsync(ct);
        }

        List<WidgetTermLink> categories = [];
        if (widgets.Any(x => x.Type == WidgetType.Categories))
        {
            var rows = await db.Categories.AsNoTracking()
                .Select(x => new
                {
                    x.Id, x.Name, x.Slug, x.ParentId,
                    Published = x.Contents.Count(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published)
                })
                .ToListAsync(ct);
            categories = OrderByHierarchy(rows.Select(x => new TermRow(x.Id, x.Name, x.Slug, x.ParentId, x.Published)));
        }

        List<WidgetTermLink> tags = [];
        if (widgets.Any(x => x.Type == WidgetType.Tags))
        {
            tags = await db.Tags.AsNoTracking()
                .Where(x => x.Contents.Any(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published))
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new WidgetTermLink(x.Name, x.Slug, 0))
                .ToListAsync(ct);
        }

        // 最新評論：與公開評論同一組可見性規則（已核可、已發布未保護未私密、本站開放評論）。
        var commentsMax = widgets.Where(x => x.Type == WidgetType.RecentComments)
            .Select(x => WidgetTypes.ParseConfig(x.ConfigJson).Count ?? 5).DefaultIfEmpty(5).Max();
        List<WidgetCommentLink> recentComments = [];
        if (widgets.Any(x => x.Type == WidgetType.RecentComments))
        {
            var rows = await db.Comments.AsNoTracking()
                .Where(c => c.Status == CommentStatus.Approved
                    && c.Content.Kind == ContentKind.Post && c.Content.Status == ContentStatus.Published
                    && !c.Content.IsProtected && !c.Content.IsPrivate
                    && (c.ParentId == null || c.Parent!.Status == CommentStatus.Approved)
                    && db.Settings.Any(s => s.Id == 1 && s.CommentsEnabled))
                .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
                .Take(commentsMax)
                .Select(c => new { c.AuthorName, c.Markdown, c.CreatedAt, c.Content.Title, c.Content.Slug })
                .ToListAsync(ct);
            recentComments = [.. rows.Select(c =>
                new WidgetCommentLink(c.AuthorName, CommentExcerpt(c.Markdown), c.CreatedAt, c.Title, c.Slug))];
        }

        List<PublicWidget> result = new(widgets.Count);
        foreach (var x in widgets)
        {
            var config = WidgetTypes.ParseConfig(x.ConfigJson);
            List<WidgetRssItem> rss = [];
            if (x.Type == WidgetType.RssFeed && config.Url is { Length: > 0 } && WebhookService.IsAllowedWebhookUrl(config.Url))
                rss = await FetchFeedItemsAsync(config.Url, Math.Min(config.Count ?? 5, Limits.WidgetRecentPosts), ct);
            result.Add(new PublicWidget(x.Id, x.Area, x.Type, x.Title, config.Count ?? 5, config.Markdown ?? "",
                recent.Take(config.Count ?? 5).ToList(), categories, tags,
                recentComments.Take(config.Count ?? 5).ToList(), rss));
        }
        return result;
    }

    /// <summary>抓取並快取外部 feed；失敗時以短暫的空清單降級，不讓小工具拖垮公開頁面。</summary>
    private async Task<List<WidgetRssItem>> FetchFeedItemsAsync(string url, int count, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync("widget-feed:" + url, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = FeedCacheTtl;
            try
            {
                var client = httpClients.CreateClient("OrigamiWidgetFeed");
                await using var remote = await client.GetStreamAsync(url, ct);
                using var bounded = new MemoryStream();
                await remote.CopyToAsync(bounded, 81920, ct);
                if (bounded.Length == 0 || bounded.Length > Limits.WidgetFeedMaxBytes) return [];
                bounded.Position = 0;
                return ParseFeed(bounded, count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is HttpRequestException or InvalidOperationException or XmlException or UriFormatException)
            {
                entry.AbsoluteExpirationRelativeToNow = FeedFailureTtl;
                return [];
            }
        }) ?? [];
    }

    /// <summary>解析 RSS 2.0 或 Atom feed；禁止 DTD 與外部實體，項目連結僅保留 http／https。</summary>
    internal static List<WidgetRssItem> ParseFeed(Stream stream, int count)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true
        });
        var document = XDocument.Load(reader);
        var rss = document.Descendants("item").Take(count)
            .Select(x => new WidgetRssItem((string?)x.Element("title") ?? "",
                (string?)x.Element("link") ?? "", ParseDate((string?)x.Element("pubDate"))))
            .Where(x => x.Title.Length > 0 && IsHttpLink(x.Url))
            .ToList();
        if (rss.Count > 0) return rss;
        XNamespace atom = "http://www.w3.org/2005/Atom";
        return [.. document.Descendants(atom + "entry").Take(count)
            .Select(x => new WidgetRssItem((string?)x.Element(atom + "title") ?? "",
                x.Elements(atom + "link").Attributes("href").FirstOrDefault()?.Value ?? "",
                ParseDate((string?)x.Element(atom + "updated") ?? (string?)x.Element(atom + "published"))))
            .Where(x => x.Title.Length > 0 && IsHttpLink(x.Url))];
    }

    /// <summary>項目連結僅接受 http／https；javascript: 等偽 URI 即使格式合法也不採用。</summary>
    private static bool IsHttpLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    /// <summary>評論預覽：剝除常見 Markdown 語法後截斷；僅作純文字顯示（Razor 會再編碼）。</summary>
    private static string CommentExcerpt(string markdown)
    {
        var text = Regex.Replace(markdown, @"\[([^\]]+)\]\(([^)]+)\)", "$1");
        text = Regex.Replace(text, @"[*_~`#>]", "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= 100 ? text : text[..100] + "…";
    }

    /// <summary>分類的階層排序：父分類在前、子分類緊隨其後（與前台導覽一致）。</summary>
    private static List<WidgetTermLink> OrderByHierarchy(IEnumerable<TermRow> rows)
    {
        var list = rows.ToList();
        var byId = list.ToDictionary(x => x.Id);
        List<WidgetTermLink> ordered = [];
        // 深度上限防護資料異常時的循環參照（資料庫僅保證不自引用）。
        void Walk(TermRow row, int depth)
        {
            if (depth > 10) return;
            ordered.Add(new WidgetTermLink(row.Name, row.Slug, depth));
            foreach (var child in list.Where(c => c.ParentId == row.Id).OrderBy(c => c.Name).ThenBy(c => c.Id))
                Walk(child, depth + 1);
        }
        foreach (var top in list.Where(x => x.ParentId is null || !byId.ContainsKey(x.ParentId.Value))
                     .OrderBy(x => x.Name).ThenBy(x => x.Id))
            Walk(top, 0);
        return ordered;
    }

    private sealed record TermRow(Guid Id, string Name, string Slug, Guid? ParentId, int Published);
}
