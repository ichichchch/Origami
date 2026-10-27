namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MediaClosureTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        bitmap.Erase(SKColors.SeaGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90)!;
        return encoded.ToArray();
    }

    private static MediaService CreateService(TestActor actor, TemporaryMediaStorage directory) => new(
        actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
        actor.Services.GetRequiredService<AdminAccess>(), directory.Create(), TimeProvider.System, NullLogger<MediaService>.Instance);

    [PostgresFact]
    public async Task Uploads_generate_variant_files_and_rendering_emits_srcset()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var service = CreateService(admin, directory);
        var images = directory.Create().Images;
        MediaItem wide;
        await using (var stream = new MemoryStream(Png(1200, 400)))
        {
            wide = await service.UploadAsync(stream, "wide.png");
        }
        var stem = Path.GetFileNameWithoutExtension(wide.FileName);
        Assert.True(File.Exists(Path.Combine(images, wide.FileName)));
        Assert.True(File.Exists(Path.Combine(images, stem + "_w480.png")));
        Assert.True(File.Exists(Path.Combine(images, stem + "_w960.png")));
        Assert.False(File.Exists(Path.Combine(images, stem + "_w1600.png")), "variants never upscale");

        MediaItem narrow;
        await using (var stream = new MemoryStream(Png(300, 200)))
        {
            narrow = await service.UploadAsync(stream, "narrow.png");
        }

        var markdown = new MarkdownService();
        var catalog = await service.PublicImageWidthsAsync();
        var html = markdown.Render("![寬圖](/media/" + wide.FileName + ")", catalog);
        Assert.Contains("srcset=\"/media/", html);
        Assert.Contains(" 480w", html);
        Assert.Contains(" 960w", html);
        Assert.DoesNotContain(" 1600w", html);
        // 窄圖沒有變體；標籤維持單純，不提供壞掉的候選。
        var narrowHtml = markdown.Render("![窄圖](/media/" + narrow.FileName + ")", catalog);
        Assert.DoesNotContain("srcset=", narrowHtml, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Referenced_images_refuse_deletion_until_the_reference_is_gone()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var service = CreateService(admin, directory);
        MediaItem uploaded;
        await using (var stream = new MemoryStream(Png(800, 300)))
        {
            uploaded = await service.UploadAsync(stream, "referenced.png");
        }
        var post = NewContent("引用圖片的文章", status: ContentStatus.Draft);
        post.Markdown = "前文\n\n![圖](/media/" + uploaded.FileName + ")\n\n後文";
        await admin.Content.SaveAsync(post);

        Assert.Equal(1, await service.CountReferencesAsync(uploaded.Id));
        var references = await service.ListReferencesAsync(uploaded.Id);
        Assert.Equal("引用圖片的文章", references.Single().Title);
        var refused = await Assert.ThrowsAsync<CmsException>(() => service.DeleteAsync(uploaded.Id, true));
        Assert.Contains("引用圖片的文章", refused.Message, StringComparison.Ordinal);

        // 進回收桶的內容在清除前仍持有引用。
        var trashed = await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Draft);
        var item = trashed.Items.Single();
        await admin.Content.TrashAsync(item.Id, item.Version, true);
        Assert.Equal(1, await service.CountReferencesAsync(uploaded.Id));
        // 進回收桶已輪替版本；永久刪除要帶新的版本。
        var trashedItem = (await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Trashed)).Items.Single();
        await admin.Content.DeleteAsync(trashedItem.Id, trashedItem.Version, true);
        Assert.Equal(0, await service.CountReferencesAsync(uploaded.Id));
        await service.DeleteAsync(uploaded.Id, true);
        var images = directory.Create().Images;
        Assert.False(File.Exists(Path.Combine(images, uploaded.FileName)));
        Assert.False(File.Exists(Path.Combine(images, MediaService.VariantName(uploaded.FileName, 480))));
        using var anonymous = App.Anonymous();
        var anonymousService = CreateService(anonymous, new TemporaryMediaStorage());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymousService.ListReferencesAsync(uploaded.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymousService.DeleteAsync(uploaded.Id, true));
    }

    [PostgresFact]
    public async Task Backfill_regenerates_missing_variants_for_existing_images()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var service = CreateService(admin, directory);
        MediaItem uploaded;
        await using (var stream = new MemoryStream(Png(1000, 500)))
        {
            uploaded = await service.UploadAsync(stream, "legacy.png");
        }
        var storage = directory.Create();
        var variant = Path.Combine(storage.Images, MediaService.VariantName(uploaded.FileName, 480));
        File.Delete(variant);
        Assert.False(File.Exists(variant));

        Assert.Equal(1, await service.BackfillVariantsAsync());
        Assert.True(File.Exists(variant));
        Assert.Equal(0, await service.BackfillVariantsAsync());
    }

    [PostgresFact]
    public async Task Site_icon_resolves_to_the_media_file_and_protects_deletion()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var service = CreateService(admin, directory);
        MediaItem icon;
        await using (var stream = new MemoryStream(Png(64, 64)))
        {
            icon = await service.UploadAsync(stream, "icon.png");
        }
        var settings = await admin.Settings.GetAsync();
        settings.SiteIconMediaId = icon.Id;
        await admin.Settings.SaveAsync(settings);

        var file = await service.GetSiteIconAsync();
        Assert.NotNull(file);
        Assert.Equal("image/png", file!.ContentType);
        Assert.True(File.Exists(file.Path), "the icon points at the stored media file");
        Assert.EndsWith(icon.FileName, file.Path, StringComparison.Ordinal);

        // 設定仍指向目前的網站圖示時，該檔案不得刪除。
        var refused = await Assert.ThrowsAsync<CmsException>(() => service.DeleteAsync(icon.Id, true));
        Assert.Contains("網站圖示", refused.Message, StringComparison.Ordinal);

        // 清除設定即釋放檔案；未設定圖示時 HTTP 回 404。
        var cleared = await admin.Settings.GetAsync();
        cleared.SiteIconMediaId = null;
        await admin.Settings.SaveAsync(cleared);
        await service.DeleteAsync(icon.Id, true);
        Assert.Null(await service.GetSiteIconAsync());
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/favicon.ico")).StatusCode);
    }
}
