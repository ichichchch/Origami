namespace Origami.Core.Services;

/// <summary>
/// 以串流將網站設定、分類標籤、完整內容與媒體中繼資料輸出為 JSON。上傳的圖片
/// 二進位資料、帳號與任何機密都刻意不進入匯出檔；內容保護雜湊隨檔同行，
/// 讓密碼門檻在匯入往返後仍可運作。
/// </summary>
public sealed class ExportService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin)
{
    public async Task WriteAsync(Stream output, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
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
}
