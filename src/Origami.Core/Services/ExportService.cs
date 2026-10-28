namespace Origami.Core.Services;

/// <summary>
/// 以串流將網站設定、分類標籤、完整內容與媒體中繼資料輸出為 JSON。上傳的圖片
/// 二進位資料、帳號與任何機密都刻意不進入匯出檔；內容保護雜湊隨檔同行，
/// 讓密碼門檻在匯入往返後仍可運作。WriteArchiveAsync 另提供
/// 「JSON＋媒體檔案」的 ZIP 封裝，供一檔完整搬遷與備份。
/// </summary>
public sealed class ExportService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, StoragePaths storage)
{
    public async Task WriteAsync(Stream output, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        await WriteCoreAsync(output, ct);
    }

    /// <summary>無授權檢查的 JSON 輸出核心；供背景備份這類系統操作複用。</summary>
    private async Task WriteCoreAsync(Stream output, CancellationToken ct)
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
        writer.WriteEndObject();

        writer.WriteStartArray("categories");
        await foreach (var category in db.Categories.AsNoTracking().OrderBy(x => x.Slug).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteString("name", category.Name);
            writer.WriteString("slug", category.Slug);
            writer.WriteString("description", category.Description);
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

        writer.WriteStartArray("contents");
        await foreach (var content in db.Contents.AsNoTracking().AsSplitQuery()
            .Include(x => x.Categories).Include(x => x.Tags).OrderBy(x => x.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", content.Kind == ContentKind.Post ? "post" : "page");
            writer.WriteString("title", content.Title);
            writer.WriteString("slug", content.Slug);
            writer.WriteString("excerpt", content.Excerpt);
            writer.WriteBoolean("isSticky", content.IsSticky);
            writer.WriteString("markdown", content.Markdown);
            writer.WriteString("status", content.Status switch
            {
                ContentStatus.Published => "published",
                ContentStatus.Trashed => "trashed",
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
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("media");
        await foreach (var asset in db.MediaAssets.AsNoTracking().OrderBy(x => x.UploadedAt).ThenBy(x => x.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteString("fileName", asset.FileName);
            writer.WriteString("originalName", asset.OriginalName);
            writer.WriteString("contentType", asset.ContentType);
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
    /// 「JSON＋媒體檔案」的 ZIP 封裝：origami-export.json 加上 media/ 目錄內的
    /// 原圖與既有變體。總量超過上限時整體拒絕，改用 JSON 匯出並手動備份圖片目錄。
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
        var fileNames = await db.MediaAssets.AsNoTracking().OrderBy(x => x.UploadedAt).ThenBy(x => x.Id)
            .Select(x => x.FileName).ToListAsync(ct);
        var archiveEntries = new List<string>();
        long totalBytes = 0;
        foreach (var fileName in fileNames)
        {
            // 儲存檔名決定路徑；不安全的舊檔名直接略過，不讓它進入封裝。
            if (!ContentRules.IsSafeMediaFileName(fileName)) continue;
            var names = new List<string> { fileName };
            foreach (var width in Limits.ImageVariantWidths) names.Add(MediaService.VariantName(fileName, width));
            foreach (var name in names)
            {
                var path = Path.Combine(storage.Images, name);
                if (!File.Exists(path)) continue;
                totalBytes += new FileInfo(path).Length;
                if (totalBytes > Limits.ArchiveMaxBytes)
                    throw new CmsException($"媒體檔案總量超過 {Limits.ArchiveMaxBytes / 1024 / 1024} MB，無法打包成 ZIP；請改用 JSON 匯出，並手動備份圖片儲存目錄。");
                archiveEntries.Add(name);
            }
        }
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var jsonEntry = zip.CreateEntry("origami-export.json", CompressionLevel.Optimal);
        await using (var jsonStream = jsonEntry.Open())
        {
            await WriteCoreAsync(jsonStream, ct);
        }
        foreach (var name in archiveEntries)
        {
            // 圖片本身已是壓縮格式；最快等級避免無謂的 CPU 消耗。
            var entry = zip.CreateEntry("media/" + name, CompressionLevel.Fastest);
            await using var source = File.OpenRead(Path.Combine(storage.Images, name));
            await using var target = entry.Open();
            await source.CopyToAsync(target, ct);
        }
    }
}
