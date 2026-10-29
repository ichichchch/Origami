namespace Origami.Core.Services;

/// <summary>
/// 以串流將網站設定、分類標籤、完整內容與媒體中繼資料輸出為 JSON。上傳的圖片
/// 二進位資料、帳號與任何機密都刻意不進入匯出檔；內容保護雜湊隨檔同行，
/// 讓密碼門檻在匯入往返後仍可運作。WriteArchiveAsync 另提供
/// 「JSON＋媒體檔案」的 ZIP 封裝，供一檔完整搬遷與備份。
/// </summary>
public sealed class ExportService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, StoragePaths storage)
{
    /// <summary>
    /// 匯出站點資料；可依內容類型（僅文章／僅頁面）與分類篩選文章。篩選匯出內容不完整，
    /// 僅供挑選性備份或搬運，不建議用於還原整站（完整遷移請用 ZIP 封裝）。
    /// </summary>
    public async Task WriteAsync(Stream output, ContentKind? kind = null, string? categorySlug = null, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        if (categorySlug is not null && kind != ContentKind.Post)
            throw new CmsException("分類篩選僅適用於文章匯出。");
        if (categorySlug is not null) categorySlug = ContentRules.NormalizeSlug(categorySlug);
        await WriteCoreAsync(output, ct, kind, categorySlug);
    }

    /// <summary>無授權檢查的 JSON 輸出核心；供背景備份這類系統操作複用（一律全站）。</summary>
    private async Task WriteCoreAsync(Stream output, CancellationToken ct, ContentKind? kind = null, string? categorySlug = null)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var writer = new Utf8JsonWriter(output);
        writer.WriteStartObject();
        writer.WriteString("format", "origami-export");
        writer.WriteNumber("version", 2);
        writer.WriteString("exportedAt", DateTimeOffset.UtcNow);

        // 內容識別碼絕不離站；slug 才是匯入時建立連結的穩定識別。
        var slugById = await db.Contents.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Slug, ct);
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        writer.WriteStartObject("site");
        writer.WriteString("name", settings?.Name ?? "");
        writer.WriteString("description", settings?.Description ?? "");
        if (settings?.PostsPerPage is { } postsPerPage) writer.WriteNumber("postsPerPage", postsPerPage);
        if (settings?.FeedItems is { } feedItems) writer.WriteNumber("feedItems", feedItems);
        if (settings?.HomePageContentId is { } homeId && slugById.TryGetValue(homeId, out var homeSlug))
            writer.WriteString("homePageSlug", homeSlug);
        if (settings?.SiteIconMediaId is { } iconId)
        {
            var iconFile = await db.MediaAssets.AsNoTracking().Where(x => x.Id == iconId)
                .Select(x => x.FileName).SingleOrDefaultAsync(ct);
            if (iconFile is not null) writer.WriteString("siteIconFileName", iconFile);
        }
        if (settings?.LogoMediaId is { } logoId)
        {
            var logoFile = await db.MediaAssets.AsNoTracking().Where(x => x.Id == logoId)
                .Select(x => x.FileName).SingleOrDefaultAsync(ct);
            if (logoFile is not null) writer.WriteString("logoFileName", logoFile);
        }
        if (settings?.CustomNavigationEnabled == true) writer.WriteBoolean("customNavigationEnabled", true);
        if (!string.IsNullOrEmpty(settings?.ColorScheme) && settings.ColorScheme != ThemePresets.ColorSchemeAuto)
            writer.WriteString("colorScheme", settings.ColorScheme);
        if (!string.IsNullOrEmpty(settings?.FooterText)) writer.WriteString("footerText", settings.FooterText);
        if (!string.IsNullOrEmpty(settings?.SocialGithubUrl)) writer.WriteString("socialGithubUrl", settings.SocialGithubUrl);
        if (!string.IsNullOrEmpty(settings?.SocialXUrl)) writer.WriteString("socialXUrl", settings.SocialXUrl);
        if (!string.IsNullOrEmpty(settings?.SocialMastodonUrl)) writer.WriteString("socialMastodonUrl", settings.SocialMastodonUrl);
        if (!string.IsNullOrEmpty(settings?.CommentBlockedWords)) writer.WriteString("commentBlockedWords", settings.CommentBlockedWords);
        if (settings?.CommentCloseDays is > 0) writer.WriteNumber("commentCloseDays", settings.CommentCloseDays);
        if (settings?.MaintenanceMode == true) writer.WriteBoolean("maintenanceMode", true);
        if (!string.IsNullOrEmpty(settings?.MaintenanceMessage)) writer.WriteString("maintenanceMessage", settings.MaintenanceMessage);
        writer.WriteEndObject();

        writer.WriteStartArray("categories");
        var catSlugById = await db.Categories.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Slug, ct);
        await foreach (var category in db.Categories.AsNoTracking().OrderBy(x => x.Slug).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteString("name", category.Name);
            writer.WriteString("slug", category.Slug);
            writer.WriteString("description", category.Description);
            if (category.ParentId is { } parentCatId && catSlugById.TryGetValue(parentCatId, out var parentCatSlug))
                writer.WriteString("parentSlug", parentCatSlug);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("tags");
        await foreach (var tag in db.Tags.AsNoTracking().OrderBy(x => x.Slug).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteString("name", tag.Name);
            writer.WriteString("slug", tag.Slug);
            writer.WriteString("description", tag.Description);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("contentTypes");
        var typeSlugById = new Dictionary<Guid, string>();
        await foreach (var type in db.ContentTypes.AsNoTracking().OrderBy(x => x.Slug).AsAsyncEnumerable().WithCancellation(ct))
        {
            typeSlugById[type.Id] = type.Slug;
            writer.WriteStartObject();
            writer.WriteString("name", type.Name);
            writer.WriteString("slug", type.Slug);
            writer.WriteString("description", type.Description);
            writer.WriteBoolean("supportsCategories", type.SupportsCategories);
            writer.WriteBoolean("supportsTags", type.SupportsTags);
            writer.WriteBoolean("supportsHierarchy", type.SupportsHierarchy);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        // 自訂分類法與詞彙以 slug 對應；詞彙另記錄所屬分類法與上層詞彙。
        var taxonomySlugById = new Dictionary<Guid, string>();
        writer.WriteStartArray("customTaxonomies");
        await foreach (var taxonomy in db.CustomTaxonomies.AsNoTracking().OrderBy(x => x.Slug).AsAsyncEnumerable().WithCancellation(ct))
        {
            taxonomySlugById[taxonomy.Id] = taxonomy.Slug;
            writer.WriteStartObject();
            writer.WriteString("name", taxonomy.Name);
            writer.WriteString("slug", taxonomy.Slug);
            writer.WriteString("description", taxonomy.Description);
            writer.WriteBoolean("isHierarchical", taxonomy.IsHierarchical);
            writer.WriteString("contentType", typeSlugById.GetValueOrDefault(taxonomy.ContentTypeId, ""));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        var termSlugById = new Dictionary<Guid, string>();
        var termTaxonomyById = new Dictionary<Guid, Guid>();
        var termParentById = new Dictionary<Guid, Guid>();
        writer.WriteStartArray("customTerms");
        await foreach (var term in db.CustomTaxonomyTerms.AsNoTracking().OrderBy(x => x.Slug).AsAsyncEnumerable().WithCancellation(ct))
        {
            termSlugById[term.Id] = term.Slug;
            termTaxonomyById[term.Id] = term.TaxonomyId;
            if (term.ParentId is { } parentId) termParentById[term.Id] = parentId;
            writer.WriteStartObject();
            writer.WriteString("name", term.Name);
            writer.WriteString("slug", term.Slug);
            writer.WriteString("description", term.Description);
            writer.WriteString("taxonomy", taxonomySlugById.GetValueOrDefault(term.TaxonomyId, ""));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("customFields");
        var fieldSlugById = new Dictionary<Guid, string>();
        await foreach (var field in db.CustomFields.AsNoTracking().OrderBy(x => x.Key).AsAsyncEnumerable().WithCancellation(ct))
        {
            fieldSlugById[field.Id] = field.Key;
            writer.WriteStartObject();
            writer.WriteString("name", field.Name);
            writer.WriteString("key", field.Key);
            writer.WriteString("fieldType", field.FieldType);
            if (field.Options.Length > 0) writer.WriteString("options", field.Options);
            writer.WriteBoolean("isRequired", field.IsRequired);
            writer.WriteNumber("position", field.Position);
            writer.WriteString("contentType", typeSlugById.GetValueOrDefault(field.ContentTypeId, ""));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("contents");
        await foreach (var content in db.Contents.AsNoTracking().AsSplitQuery()
            .Include(x => x.Categories).Include(x => x.Tags).Include(x => x.CustomTerms).ThenInclude(x => x.Term)
            .Include(x => x.FieldValues).ThenInclude(x => x.Field)
            .OrderBy(x => x.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            // 篩選匯出：類型不符或（文章模式下）不含指定分類者不進入檔案。
            if (kind is { } kindFilter && content.Kind != kindFilter) continue;
            if (categorySlug is { } category && !content.Categories.Any(x => x.Slug == category)) continue;
            writer.WriteStartObject();
            writer.WriteString("kind", content.Kind == ContentKind.Post ? "post" : "page");
            writer.WriteString("title", content.Title);
            writer.WriteString("slug", content.Slug);
            // 帳號識別不離站；匯出署名快照，匯入後 byline 不因缺少帳號而消失。
            if (content.AuthorName.Length > 0) writer.WriteString("authorName", content.AuthorName);
            writer.WriteString("excerpt", content.Excerpt);
            writer.WriteBoolean("isSticky", content.IsSticky);
            if (!content.AllowComments) writer.WriteBoolean("allowComments", false);
            if (content.MenuOrder != 0) writer.WriteNumber("menuOrder", content.MenuOrder);
            if (content.MetaDescription.Length > 0) writer.WriteString("metaDescription", content.MetaDescription);
            if (content.NoIndex) writer.WriteBoolean("noIndex", true);
            writer.WriteString("markdown", content.Markdown);
            writer.WriteString("status", content.Status switch
            {
                ContentStatus.Published => "published",
                ContentStatus.Trashed => "trashed",
                ContentStatus.Pending => "pending",
                _ => "draft",
            });
            if (content.IsProtected)
            {
                writer.WriteBoolean("isProtected", true);
                writer.WriteString("protectionHash", content.ProtectionHash);
            }
            if (content.IsPrivate) writer.WriteBoolean("isPrivate", true);
            if (content.ParentId is { } parentId && slugById.TryGetValue(parentId, out var parentSlug))
                writer.WriteString("parentSlug", parentSlug);
            writer.WriteString("createdAt", content.CreatedAt);
            writer.WriteString("updatedAt", content.UpdatedAt);
            if (content.PublishedAt is { } published) writer.WriteString("publishedAt", published);
            if (content.PublishAt is { } scheduled) writer.WriteString("publishAt", scheduled);
            if (content.DeletedAt is { } deleted) writer.WriteString("deletedAt", deleted);
            writer.WriteStartArray("categories");
            foreach (var slug in content.Categories.Select(x => x.Slug).OrderBy(x => x, StringComparer.Ordinal)) writer.WriteStringValue(slug);
            writer.WriteEndArray();
            writer.WriteStartArray("tags");
            foreach (var slug in content.Tags.Select(x => x.Slug).OrderBy(x => x, StringComparer.Ordinal)) writer.WriteStringValue(slug);
            writer.WriteEndArray();
            // 自訂類型與詞彙以 slug 對應；匯入時依 slug 還原對應類型。
            if (content.ContentTypeId is { } contentTypeId && typeSlugById.TryGetValue(contentTypeId, out var contentTypeSlug))
                writer.WriteString("contentType", contentTypeSlug);
            writer.WriteStartArray("terms");
            foreach (var slug in content.CustomTerms.Select(x => x.Term.Slug).OrderBy(x => x, StringComparer.Ordinal)) writer.WriteStringValue(slug);
            writer.WriteEndArray();
            if (content.FieldValues.Count > 0)
            {
                writer.WriteStartObject("fieldValues");
                foreach (var value in content.FieldValues.OrderBy(x => x.Field.Key, StringComparer.Ordinal))
                    writer.WriteString(value.Field.Key, value.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("comments");
        var slugByContentId = slugById;
        await foreach (var comment in db.Comments.AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .AsAsyncEnumerable().WithCancellation(ct))
        {
            // 待審與垃圾評論不隨檔案搬遷；僅保留已核可的公開互動。
            if (comment.Status != CommentStatus.Approved) continue;
            if (!slugByContentId.TryGetValue(comment.ContentId, out var contentSlug)) continue;
            writer.WriteStartObject();
            writer.WriteString("contentSlug", contentSlug);
            writer.WriteString("authorName", comment.AuthorName);
            writer.WriteString("url", comment.Url);
            writer.WriteString("markdown", comment.Markdown);
            writer.WriteString("createdAt", comment.CreatedAt);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("navigation");
        await foreach (var item in db.SiteNavItems.AsNoTracking().OrderBy(x => x.Position).ThenBy(x => x.Id)
            .AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteString("title", item.Title);
            writer.WriteString("url", item.Url);
            writer.WriteBoolean("isVisible", item.IsVisible);
            writer.WriteNumber("position", item.Position);
            // 站內目標以頁面代稱表示，匯入時才解析成新的內容識別。
            if (item.ContentId is { } target && slugById.TryGetValue(target, out var pageSlug))
                writer.WriteString("pageSlug", pageSlug);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("media");        await foreach (var asset in db.MediaAssets.AsNoTracking().OrderBy(x => x.UploadedAt).ThenBy(x => x.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteString("fileName", asset.FileName);
            writer.WriteString("originalName", asset.OriginalName);
            writer.WriteString("contentType", asset.ContentType);
            writer.WriteString("kind", asset.Kind switch
            {
                MediaKind.Document => "document",
                MediaKind.Audio => "audio",
                MediaKind.Video => "video",
                _ => "image"
            });
            writer.WriteString("altText", asset.AltText);
            writer.WriteNumber("size", asset.Size);
            writer.WriteNumber("width", asset.Width);
            writer.WriteNumber("height", asset.Height);
            writer.WriteString("uploadedAt", asset.UploadedAt);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        await writer.FlushAsync(ct);
    }

    /// <summary>
    /// 「JSON＋媒體檔案」的 ZIP 封裝：origami-export.json 加上 media/（圖片與變體）
    /// 與 files/（PDF／音訊／影片原檔）。總量超過上限時整體拒絕，改用 JSON 匯出並手動備份媒體目錄。
    /// </summary>
    public async Task WriteArchiveAsync(Stream output, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        await WriteArchiveUncheckedAsync(output, ct);
    }

    /// <summary>
    /// 無授權檢查的封裝核心；僅供背景備份這類系統操作使用，
    /// 一般請求路徑必須走 <see cref="WriteArchiveAsync"/>。
    /// </summary>
    internal async Task WriteArchiveUncheckedAsync(Stream output, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var assets = await db.MediaAssets.AsNoTracking().OrderBy(x => x.UploadedAt).ThenBy(x => x.Id)
            .Select(x => new { x.FileName, x.Kind }).ToListAsync(ct);
        var archiveEntries = new List<(string SourceDirectory, string Name, string EntryPath)>();
        long totalBytes = 0;
        void Add(string directory, string name, string entryPath)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) return;
            totalBytes += new FileInfo(path).Length;
            if (totalBytes > Limits.ArchiveMaxBytes)
                throw new CmsException($"媒體檔案總量超過 {Limits.ArchiveMaxBytes / 1024 / 1024} MB，無法打包成 ZIP；請改用 JSON 匯出，並手動備份媒體儲存目錄。");
            archiveEntries.Add((directory, name, entryPath));
        }
        foreach (var asset in assets)
        {
            // 儲存檔名決定路徑；不安全的舊檔名直接略過，不讓它進入封裝。
            if (asset.Kind != MediaKind.Image)
            {
                if (ContentRules.IsSafeMediaFileNameAny(asset.FileName))
                    Add(storage.Files, asset.FileName, "files/" + asset.FileName);
                continue;
            }
            if (!ContentRules.IsSafeMediaFileName(asset.FileName)) continue;
            var names = new List<string> { asset.FileName };
            foreach (var width in Limits.ImageVariantWidths) names.Add(MediaService.VariantName(asset.FileName, width));
            foreach (var name in names) Add(storage.Images, name, "media/" + name);
        }
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var jsonEntry = zip.CreateEntry("origami-export.json", CompressionLevel.Optimal);
        await using (var jsonStream = jsonEntry.Open())
        {
            await WriteCoreAsync(jsonStream, ct);
        }
        foreach (var (sourceDirectory, name, entryPath) in archiveEntries)
        {
            // 媒體檔本身多為已壓縮格式；最快等級避免無謂的 CPU 消耗。
            var entry = zip.CreateEntry(entryPath, CompressionLevel.Fastest);
            await using var source = File.OpenRead(Path.Combine(sourceDirectory, name));
            await using var target = entry.Open();
            await source.CopyToAsync(target, ct);
        }
    }
}
