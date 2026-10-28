namespace Origami.Core.Services;

public sealed class ImportService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider clock, StoragePaths storage)
{
    public sealed record ImportPreview(string SiteName, int Categories, int Tags, int Posts, int Pages, int Media);

    /// <summary>統計匯入將還原的內容，不動任何資料。接受 JSON 或「JSON＋媒體」的 ZIP。</summary>
    public async Task<ImportPreview> AnalyzeAsync(Stream input, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        using var payload = await BufferAsync(input, ct);
        using var parsed = await ParseContainerAsync(payload, ct);
        return PreviewOf(parsed.Document);
    }

    /// <summary>
    /// 還原式匯入：在單一交易內以匯出檔取代現行設定、分類標籤、內容與媒體
    /// 中繼資料。帳號一律不在此檔案內。JSON 模式不改動圖片檔案；
    /// ZIP 模式（完整備份）另在交易成功後還原圖片並清理清單外的孤兒檔。
    /// </summary>
    public async Task<ImportPreview> ApplyAsync(Stream input, bool confirmed, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        if (!confirmed) throw new CmsException("請先確認匯入將取代站內全部文字資料。");
        using var payload = await BufferAsync(input, ct);
        using var parsed = await ParseContainerAsync(payload, ct);
        if (parsed.Archive is not null) ValidateArchiveFiles(parsed.Archive, parsed.Document);
        var preview = await ApplyCoreAsync(parsed.Document, ct);
        if (parsed.Archive is not null) await RestoreArchiveFilesAsync(parsed.Archive, parsed.Document, ct);
        return preview;
    }

    private static async Task<MemoryStream> BufferAsync(Stream input, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        if (buffer.Length is 0 or > Limits.ArchiveMaxBytes)
            throw new CmsException($"匯入檔大小須為 1 位元組至 {Limits.ArchiveMaxBytes} 位元組。");
        return buffer;
    }

    /// <summary>逐區塊複製並累計解壓後位元組數，超過封裝上限立即中止；ZipArchive 條目宣稱的大小不可信。</summary>
    private static async Task<long> CopyBoundedAsync(Stream source, Stream target, long totalBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) return totalBytes;
            totalBytes += read;
            if (totalBytes > Limits.ArchiveMaxBytes)
                throw new CmsException($"封裝解壓後總量超過 {Limits.ArchiveMaxBytes / 1024 / 1024} MB 上限，請確認檔案由本站匯出且未遭竄改。");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private sealed class ParsedImport(JsonDocument document, ZipArchive? archive) : IDisposable
    {
        public JsonDocument Document = document;
        public ZipArchive? Archive = archive;
        public void Dispose()
        {
            Document.Dispose();
            Archive?.Dispose();
        }
    }

    /// <summary>依魔數分辨容器：PK 開頭為 ZIP（內含 origami-export.json），其餘視為 JSON。</summary>
    private static async Task<ParsedImport> ParseContainerAsync(MemoryStream payload, CancellationToken ct)
    {
        var bytes = payload.GetBuffer();
        if (payload.Length >= 2 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K')
        {
            payload.Position = 0;
            var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry("origami-export.json")
                ?? throw new CmsException("ZIP 內缺少 origami-export.json，請使用本站產生的完整備份（ZIP）或 JSON 匯出檔。");
            await using var json = entry.Open();
            // 高壓縮比的偽造條目可能在解壓時爆炸；JSON 條目解壓後同受封裝上限約束。
            using var expanded = new MemoryStream();
            await CopyBoundedAsync(json, expanded, 0, ct);
            expanded.Position = 0;
            return new(await ParseJsonAsync(expanded, ct), archive);
        }
        payload.Position = 0;
        return new(await ParseJsonAsync(payload, ct), null);
    }

    /// <summary>清單內的每個媒體都必須在 ZIP 有對應檔案；缺檔在動資料庫之前整體拒絕。</summary>
    private static void ValidateArchiveFiles(ZipArchive archive, JsonDocument document)
    {
        List<string> missing = [];
        foreach (var node in document.RootElement.GetProperty("media").EnumerateArray())
        {
            var fileName = node.GetString("fileName");
            if (fileName is null || !ContentRules.IsSafeMediaFileName(fileName)) continue;
            if (archive.GetEntry("media/" + fileName) is null) missing.Add(fileName);
        }
        if (missing.Count > 0)
        {
            var sample = string.Join("、", missing.Take(3));
            var more = missing.Count > 3 ? $" 等共 {missing.Count} 個" : "";
            throw new CmsException($"ZIP 缺少媒體檔案，無法完整還原：{sample}{more}。");
        }
    }

    /// <summary>交易成功後把 ZIP 內的圖片還原到儲存目錄，並清理清單外的孤兒檔。</summary>
    private async Task RestoreArchiveFilesAsync(ZipArchive archive, JsonDocument document, CancellationToken ct)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in document.RootElement.GetProperty("media").EnumerateArray())
        {
            var fileName = node.GetString("fileName");
            if (fileName is null || !ContentRules.IsSafeMediaFileName(fileName)) continue;
            expected.Add(fileName);
            foreach (var width in Limits.ImageVariantWidths)
                expected.Add(MediaService.VariantName(fileName, width));
        }
        // 固定本次作業的當站目錄；解壓與孤兒清理都不列舉全域 Root 或其他站點。
        var images = storage.Images;
        var stagingRoot = Path.Combine(storage.Staging, "import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var staged = new List<(string Temp, string Final)>();
            // 解壓後的累計位元組數；條目標頭宣稱的大小不可信，以實際讀取為準。
            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith("media/", StringComparison.Ordinal)) continue;
                var name = entry.FullName["media/".Length..];
                // 清單與變體以外的項目一律不落地，檔名也必須仍是安全的圖片名。
                if (!expected.Contains(name) || !ContentRules.IsSafeMediaFileName(name)) continue;
                var temp = Path.Combine(stagingRoot, name);
                await using (var source = entry.Open())
                await using (var target = File.Create(temp))
                    totalBytes = await CopyBoundedAsync(source, target, totalBytes, ct);
                staged.Add((temp, Path.Combine(images, name)));
            }
            // 解壓全部成功後才覆寫正式檔案；單檔失敗會回報但資料庫還原不受影響。
            var failed = new List<string>();
            foreach (var (temp, final) in staged)
            {
                try { File.Move(temp, final, overwrite: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(Path.GetFileName(final)); }
            }
            if (failed.Count > 0)
                throw new CmsException($"資料已還原，但以下圖片檔案寫入失敗，請檢查儲存目錄權限後重試：{string.Join("、", failed.Take(5))}。");
        }
        finally
        {
            try { Directory.Delete(stagingRoot, recursive: true); } catch (IOException) { /* 暫存清理盡力而為。 */ }
        }
        // 取代式還原：僅清理當站圖片目錄內、不在清單（含變體）內的舊圖檔，不遞迴。
        foreach (var path in Directory.EnumerateFiles(images))
        {
            var name = Path.GetFileName(path);
            if (!expected.Contains(name) && ContentRules.IsSafeMediaFileName(name))
            {
                try { File.Delete(path); } catch (IOException) { /* 清理盡力而為。 */ }
            }
        }
    }

    /// <summary>
    /// 還原式匯入的核心：在單一交易內以匯出資料取代現行設定、分類標籤、
    /// 內容與媒體中繼資料；已發布內容重新進入嵌入管道。
    /// </summary>
    private async Task<ImportPreview> ApplyCoreAsync(JsonDocument document, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();

        // 複合 Restrict FK 不可由資料庫清空 SiteId；先解除當站設定、階層與封面引用。
        // ExecuteUpdate／ExecuteDelete 均保留 query filters，不得清除其他站點的資料。
        await db.Settings.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.HomePageContentId, (Guid?)null)
            .SetProperty(x => x.SiteIconMediaId, (Guid?)null), ct);
        await db.Contents.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.ParentId, (Guid?)null)
            .SetProperty(x => x.CoverMediaId, (Guid?)null), ct);
        // 關聯表、嵌入、工作、修訂版本、瀏覽與轉址隨當站內容串聯刪除。
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
                SiteId = db.CurrentSiteId,
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
                SiteId = db.CurrentSiteId,
                Name = Bounded(node, "name", Limits.TaxonomyName, "標籤名稱"),
                Slug = Bounded(node, "slug", Limits.Slug, "標籤代稱"),
                Description = Bounded(node, "description", Limits.TaxonomyDescription, "標籤描述", optional: true),
                Version = Guid.NewGuid(),
            };
            if (!tagsBySlug.TryAdd(tag.Slug, tag)) throw new CmsException($"匯入的標籤代稱重複：{tag.Slug}。");
            db.Tags.Add(tag);
        }
        Dictionary<string, MediaAsset> mediaByFileName = [];
        foreach (var node in document.RootElement.GetProperty("media").EnumerateArray())
        {
            var fileName = Bounded(node, "fileName", 80, "媒體檔名");
            // 儲存檔名稍後會變成檔案路徑；僅允許匯入單純的圖片檔名。
            if (!ContentRules.IsSafeMediaFileName(fileName))
                throw new CmsException($"匯入的媒體檔名不合法：{fileName}。");
            if (!mediaByFileName.TryAdd(fileName, new MediaAsset
            {
                Id = Guid.NewGuid(),
                SiteId = db.CurrentSiteId,
                FileName = fileName,
                OriginalName = Bounded(node, "originalName", 200, "媒體原始名稱"),
                ContentType = Bounded(node, "contentType", 40, "媒體類型"),
                AltText = Bounded(node, "altText", Limits.AltText, "媒體替代文字", optional: true),
                Size = node.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                Width = node.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number ? width.GetInt32() : 0,
                Height = node.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number ? height.GetInt32() : 0,
                UploadedAt = Date(node, "uploadedAt") ?? now,
            }))
                throw new CmsException($"匯入的媒體檔名重複：{fileName}。");
        }
        foreach (var asset in mediaByFileName.Values) db.MediaAssets.Add(asset);

        // 第一輪：建立所有內容資料列；階層連結在記憶體中的實體上解析。
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
                SiteId = db.CurrentSiteId,
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
                content.PublishedAt = now; // 資料庫檢查約束要求現行發布狀態必須有日期
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
        // 第二輪：連結頁面上層，並以一次記憶體內的 slug 鏈走訪拒絕循環。
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
        // 匯入的媒體資料列會取得新識別碼，因此圖示改從檔名解析。
        Guid? siteIconId = null;
        if (site.GetString("siteIconFileName") is { } iconFile && mediaByFileName.TryGetValue(iconFile, out var icon))
            siteIconId = icon.Id;
        settings.SiteIconMediaId = siteIconId;
        settings.Version = Guid.NewGuid();

        // 已發布的公開內容與全新發布一樣，重新進入嵌入管道；站點僅由伺服器指定。
        foreach (var content in contentsBySlug.Values.Where(x => x.Status == ContentStatus.Published && !x.IsProtected && !x.IsPrivate))
            db.EmbeddingJobs.Add(new EmbeddingJob { SiteId = db.CurrentSiteId, ContentId = content.Id, EnqueuedAt = now, NextAttemptAt = now });

        await ContentRules.SaveAsync(db, ct);
        await transaction.CommitAsync(ct);
        return PreviewOf(document);
    }

    private static async Task<JsonDocument> ParseJsonAsync(Stream input, CancellationToken ct)
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
