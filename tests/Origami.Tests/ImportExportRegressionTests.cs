namespace Origami.Tests;

/// <summary>
/// 資料匯入、網站圖示與媒體引用保護的回歸測試。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportExportRegressionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Import_refuses_media_names_that_would_leave_the_image_directory()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("匯入前文章", status: ContentStatus.Published));
        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var document = JsonNode.Parse(buffer.ToArray())!;
        document["media"] = new JsonArray(new JsonObject
        {
            ["fileName"] = "../escape.png",
            ["originalName"] = "escape.png",
            ["contentType"] = "image/png",
            ["altText"] = "",
            ["size"] = 10,
            ["width"] = 10,
            ["height"] = 10,
            ["uploadedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        });

        var importer = admin.Services.GetRequiredService<ImportService>();
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
        var refused = await Assert.ThrowsAsync<CmsException>(() => importer.ApplyAsync(input, confirmed: true));
        Assert.Contains("媒體檔名", refused.Message, StringComparison.Ordinal);

        // 被拒絕的匯入不得改動任何資料。
        await using var db = await App.CreateContextAsync();
        Assert.True(await db.Contents.AnyAsync(x => x.Slug == "匯入前文章" || x.Title == "匯入前文章"));
    }

    [PostgresFact]
    public async Task Import_restores_the_site_icon_to_the_imported_media()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var media = CreateMediaService(admin, directory);
        MediaItem icon;
        using (var upload = new MemoryStream(Png(64, 64)))
        {
            icon = await media.UploadAsync(upload, "icon.png");
        }
        var settings = await admin.Settings.GetAsync();
        settings.SiteIconMediaId = icon.Id;
        await admin.Settings.SaveAsync(settings);

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var importer = admin.Services.GetRequiredService<ImportService>();
        using (var input = new MemoryStream(buffer.ToArray()))
        {
            await importer.ApplyAsync(input, confirmed: true);
        }

        var restored = await admin.Settings.GetAsync();
        Assert.NotNull(restored.SiteIconMediaId);
        var restoredIcon = (await media.ListAsync()).Items.Single(x => x.Id == restored.SiteIconMediaId);
        Assert.Equal(icon.FileName, restoredIcon.FileName);
        var file = await media.GetSiteIconAsync();
        Assert.NotNull(file);
    }

    [PostgresFact]
    public async Task A_referenced_variant_protects_the_original_image()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var media = CreateMediaService(admin, directory);
        MediaItem uploaded;
        using (var upload = new MemoryStream(Png(1200, 400)))
        {
            uploaded = await media.UploadAsync(upload, "variant.png");
        }
        var variant = MediaService.VariantName(uploaded.FileName, 480);
        var post = NewContent("引用縮圖的文章", status: ContentStatus.Published);
        post.Markdown = "![縮圖](/media/" + variant + ")";
        await admin.Content.SaveAsync(post);

        Assert.Equal(1, await media.CountReferencesAsync(uploaded.Id));
        var refused = await Assert.ThrowsAsync<CmsException>(() => media.DeleteAsync(uploaded.Id, confirmed: true));
        Assert.Contains("引用縮圖的文章", refused.Message, StringComparison.Ordinal);
    }

    private static MediaService CreateMediaService(TestActor actor, TemporaryMediaStorage directory) => new(
        actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
        actor.Services.GetRequiredService<AdminAccess>(), directory.Create(), TimeProvider.System, NullLogger<MediaService>.Instance);

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        bitmap.Erase(SKColors.SeaGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90)!;
        return encoded.ToArray();
    }
}
