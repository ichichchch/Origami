namespace Origami.Web.Services;

public sealed class ImportService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider clock)
{
    public sealed record ImportPreview(string SiteName, int Categories, int Tags, int Posts, int Pages, int Media);

    /// <summary>Counts what an import would restore, without touching any data.</summary>
    public async Task<ImportPreview> AnalyzeAsync(Stream input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        using var document = await ParseAsync(input, ct);
        return PreviewOf(document);
    }

    /// <summary>
    /// Restore-style import: within one transaction the current settings, taxonomies, content and media
    /// metadata are replaced by the export. Image binaries and accounts are never part of the file;
    /// operators restore the storage directory alongside, as documented.
    /// </summary>
    public async Task<ImportPreview> ApplyAsync(Stream input, bool confirmed, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (!confirmed) throw new CmsException("請先確認匯入將取代站內全部文字資料。");
        using var document = await ParseAsync(input, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();

        // Replace means replace: the join tables, embeddings, jobs, revisions, views and redirects
        // all cascade from their content rows, so a single bulk delete clears them.
        await db.Contents.ExecuteDeleteAsync(ct);
        await db.Categories.ExecuteDeleteAsync(ct);
        await db.Tags.ExecuteDeleteAsync(ct);
        await db.MediaAssets.ExecuteDeleteAsync(ct);

        var site = document.RootElement.GetProperty("site");
        Dictionary<string, Category> categoriesBySlug = [];
        foreach (var node in document.RootElement.GetProperty("categories").EnumerateArray())
        {
            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = Bounded(node, "name", Limits.TaxonomyName, "分類名稱"),
                Slug = Bounded(node, "slug", Limits.Slug, "分類代稱"),
                Description = Bounded(node, "description", Limits.TaxonomyDescription, "分類描述", optional: true),
                Version = Guid.NewGuid(),
            };
            if (!categoriesBySlug.TryAdd(category.Slug, category)) throw new CmsException($"匯入的分類代稱重複：{category.Slug}。");
            db.Categories.Add(category);
        }
        Dictionary<string, Tag> tagsBySlug = [];
        foreach (var node in document.RootElement.GetProperty("tags").EnumerateArray())
        {
            var tag = new Tag
            {
                Id = Guid.NewGuid(),
                Name = Bounded(node, "name", Limits.TaxonomyName, "標籤名稱"),
                Slug = Bounded(node, "slug", Limits.Slug, "標籤代稱"),
                Description = Bounded(node, "description", Limits.TaxonomyDescription, "標籤描述", optional: true),
                Version = Guid.NewGuid(),
            };
            if (!tagsBySlug.TryAdd(tag.Slug, tag)) throw new CmsException($"匯入的標籤代稱重複：{tag.Slug}。");
            db.Tags.Add(tag);
        }
        foreach (var node in document.RootElement.GetProperty("media").EnumerateArray())
        {
            db.MediaAssets.Add(new MediaAsset
            {
                Id = Guid.NewGuid(),
                FileName = Bounded(node, "fileName", 80, "媒體檔名"),
                OriginalName = Bounded(node, "originalName", 200, "媒體原始名稱"),
                ContentType = Bounded(node, "contentType", 40, "媒體類型"),
                AltText = Bounded(node, "altText", Limits.AltText, "媒體替代文字", optional: true),
                Size = node.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                Width = node.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number ? width.GetInt32() : 0,
                Height = node.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number ? height.GetInt32() : 0,
                UploadedAt = Date(node, "uploadedAt") ?? now,
            });
        }

        // First pass: create every content row; hierarchy links resolve on the in-memory entities.
        Dictionary<string, Content> contentsBySlug = [];
        List<(Content Entity, string? ParentSlug)> hierarchy = [];
        foreach (var node in document.RootElement.GetProperty("contents").EnumerateArray())
        {
            var kind = node.GetString("kind") == "page" ? ContentKind.Page : ContentKind.Post;
            var status = node.GetString("status") switch
            {
                "published" => ContentStatus.Published,
                "trashed" => ContentStatus.Trashed,
                _ => ContentStatus.Draft,
            };
            var content = new Content
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                Title = Bounded(node, "title", Limits.Title, "內容標題"),
                Slug = Bounded(node, "slug", Limits.Slug, "內容代稱"),
                Markdown = Bounded(node, "markdown", Limits.Body, "內容本文", optional: true),
                Excerpt = Bounded(node, "excerpt", Limits.Excerpt, "內容摘要", optional: true),
                IsSticky = node.TryGetProperty("isSticky", out var sticky) && sticky.GetBoolean(),
                IsProtected = node.TryGetProperty("isProtected", out var isProtected) && isProtected.GetBoolean(),
                IsPrivate = node.TryGetProperty("isPrivate", out var isPrivate) && isPrivate.GetBoolean(),
                ProtectionHash = node.TryGetProperty("protectionHash", out var hash) ? hash.GetString() ?? "" : "",
                Status = status,
                CreatedAt = Date(node, "createdAt") ?? now,
                UpdatedAt = Date(node, "updatedAt") ?? now,
                PublishedAt = Date(node, "publishedAt"),
                PublishAt = status == ContentStatus.Draft ? Date(node, "publishAt") : null,
                DeletedAt = status == ContentStatus.Trashed ? Date(node, "deletedAt") ?? now : null,
                Version = Guid.NewGuid(),
            };
            if (content.Status == ContentStatus.Published && content.PublishedAt is null)
                content.PublishedAt = now; // the check constraint requires a date on the live published state
            if (content.IsProtected && content.ProtectionHash == "")
                throw new CmsException($"受保護內容「{content.Title}」缺少保護資料，無法還原。");
            if (!contentsBySlug.TryAdd(content.Slug, content)) throw new CmsException($"匯入的內容代稱重複：{content.Slug}。");
            if (content.Kind == ContentKind.Post && node.TryGetProperty("categories", out var categorySlugs))
                content.Categories = [.. categorySlugs.EnumerateArray().Select(x => x.GetString() ?? "")
                    .Select(slug => categoriesBySlug.GetValueOrDefault(slug) ?? throw new CmsException($"內容「{content.Title}」引用了不存在的分類：{slug}。"))];
            if (content.Kind == ContentKind.Post && node.TryGetProperty("tags", out var tagSlugs))
                content.Tags = [.. tagSlugs.EnumerateArray().Select(x => x.GetString() ?? "")
                    .Select(slug => tagsBySlug.GetValueOrDefault(slug) ?? throw new CmsException($"內容「{content.Title}」引用了不存在的標籤：{slug}。"))];
            if (content.Kind == ContentKind.Page && node.TryGetProperty("parentSlug", out var parent) && parent.ValueKind == JsonValueKind.String)
            {
                var parentSlug = parent.GetString();
                if (parentSlug == content.Slug) throw new CmsException($"頁面「{content.Title}」不可作為自己的上層。");
                hierarchy.Add((content, parentSlug));
            }
            db.Contents.Add(content);
        }
        // Second pass: link page parents and refuse cycles with one in-memory walk over the slug chain.
        var parentSlugBySlug = hierarchy.Where(x => x.ParentSlug is not null).ToDictionary(x => x.Entity.Slug, x => x.ParentSlug!);
        foreach (var (entity, parentSlug) in hierarchy)
        {
            var parent = parentSlug is not null ? contentsBySlug.GetValueOrDefault(parentSlug) : null;
            if (parent is null) throw new CmsException($"頁面「{entity.Title}」的上層頁面不在匯入資料中：{parentSlug}。");
            if (parent.Kind != ContentKind.Page) throw new CmsException($"頁面「{entity.Title}」的上層不是獨立頁面。");
            var guard = 0;
            for (var cursorSlug = parentSlug; cursorSlug is not null; cursorSlug = parentSlugBySlug.GetValueOrDefault(cursorSlug))
            {
                if (cursorSlug == entity.Slug) throw new CmsException($"頁面「{entity.Title}」的階層形成循環，無法匯入。");
                if (++guard > 1000) throw new CmsException("匯入的頁面階層過深，請檢查上層設定。");
            }
            entity.ParentId = parent.Id;
        }

        var settings = await db.Settings.SingleAsync(x => x.Id == 1, ct);
        settings.Name = Bounded(site, "name", Limits.SiteName, "網站名稱");
        settings.Description = Bounded(site, "description", Limits.Description, "網站說明", optional: true);
        settings.PostsPerPage = site.TryGetProperty("postsPerPage", out var postsPerPage) && postsPerPage.ValueKind == JsonValueKind.Number
            ? Math.Clamp(postsPerPage.GetInt32(), 1, Limits.MaxPageSize) : null;
        settings.FeedItems = site.TryGetProperty("feedItems", out var feedItems) && feedItems.ValueKind == JsonValueKind.Number
            ? Math.Clamp(feedItems.GetInt32(), 1, Limits.MaxFeedItems) : null;
        settings.HomePageContentId = site.GetString("homePageSlug") is { } homeSlug && contentsBySlug.GetValueOrDefault(homeSlug) is { Kind: ContentKind.Page, Status: ContentStatus.Published, IsProtected: false, IsPrivate: false } home
            ? home.Id : null;
        settings.SiteIconMediaId = site.GetString("siteIconFileName") is { } iconFile
            ? await db.MediaAssets.AsNoTracking().Where(x => x.FileName == iconFile).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
            : null;
        settings.Version = Guid.NewGuid();

        // Published public content re-enters the embedding pipeline exactly like a fresh publish.
        foreach (var content in contentsBySlug.Values.Where(x => x.Status == ContentStatus.Published && !x.IsProtected && !x.IsPrivate))
            db.EmbeddingJobs.Add(new EmbeddingJob { ContentId = content.Id, EnqueuedAt = now, NextAttemptAt = now });

        await ContentRules.SaveAsync(db, ct);
        await transaction.CommitAsync(ct);
        return PreviewOf(document);
    }

    private static async Task<JsonDocument> ParseAsync(Stream input, CancellationToken ct)
    {
        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(input, cancellationToken: ct); }
        catch (JsonException) { throw new CmsException("匯入檔不是有效的 JSON，請確認使用本站匯出的資料檔。"); }
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.GetString("format") != "origami-export"
            || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
            || version.GetInt32() is < 1 or > 2
            || !root.TryGetProperty("site", out var siteNode) || siteNode.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("categories", out var categoriesNode) || categoriesNode.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("tags", out var tagsNode) || tagsNode.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("contents", out var contentsNode) || contentsNode.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("media", out var mediaNode) || mediaNode.ValueKind != JsonValueKind.Array)
            throw new CmsException("匯入檔格式不符，請使用本站「資料匯出」產生的 JSON 檔。");
        return document;
    }

    private static ImportPreview PreviewOf(JsonDocument document)
    {
        var contents = document.RootElement.GetProperty("contents").EnumerateArray().ToList();
        return new(
            document.RootElement.GetProperty("site").GetString("name") ?? "",
            document.RootElement.GetProperty("categories").GetArrayLength(),
            document.RootElement.GetProperty("tags").GetArrayLength(),
            contents.Count(x => x.GetString("kind") != "page"),
            contents.Count(x => x.GetString("kind") == "page"),
            document.RootElement.GetProperty("media").GetArrayLength());
    }

    private static string Bounded(JsonElement node, string property, int limit, string label, bool optional = false)
    {
        if (!node.TryGetProperty(property, out var value))
            return optional ? "" : throw new CmsException($"匯入資料缺少 {label}。");
        var text = value.GetString() ?? "";
        if (text.Length > limit) throw new CmsException($"{label}超過 {limit} 字上限，無法匯入。");
        return text;
    }

    private static DateTimeOffset? Date(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed : null;
}
