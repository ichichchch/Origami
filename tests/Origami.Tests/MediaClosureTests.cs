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
        // A narrow image has no variants; its tag stays plain instead of offering broken candidates.
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

        // Trashed content still owns its reference until purged.
        var trashed = await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Draft);
        var item = trashed.Items.Single();
        await admin.Content.TrashAsync(item.Id, item.Version, true);
        Assert.Equal(1, await service.CountReferencesAsync(uploaded.Id));
        // Trashing rotated the version; permanent deletion carries the fresh one.
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
}
