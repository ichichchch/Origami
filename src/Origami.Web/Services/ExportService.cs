namespace Origami.Web.Services;

/// <summary>
/// Streams the site's settings, taxonomies, full content and media metadata as JSON. Uploaded image
/// binaries, accounts and every kind of secret stay out of the export on purpose.
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
        writer.WriteNumber("version", 1);
        writer.WriteString("exportedAt", DateTimeOffset.UtcNow);

        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        writer.WriteStartObject("site");
        writer.WriteString("name", settings?.Name ?? "");
        writer.WriteString("description", settings?.Description ?? "");
        writer.WriteEndObject();

        writer.WriteStartArray("categories");
        await foreach (var category in db.Categories.AsNoTracking().OrderBy(x => x.Slug).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteString("name", category.Name);
            writer.WriteString("slug", category.Slug);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("tags");
        await foreach (var tag in db.Tags.AsNoTracking().OrderBy(x => x.Slug).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteString("name", tag.Name);
            writer.WriteString("slug", tag.Slug);
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
            writer.WriteString("status", content.Status == ContentStatus.Published ? "published" : "draft");
            writer.WriteString("createdAt", content.CreatedAt);
            writer.WriteString("updatedAt", content.UpdatedAt);
            if (content.PublishedAt is { } published) writer.WriteString("publishedAt", published);
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
