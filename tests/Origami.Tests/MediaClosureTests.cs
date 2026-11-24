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

    private static byte[] JpegWithOrientation(int width, int height, SKEncodedOrigin origin)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        bitmap.Erase(SKColors.SeaGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90)!;
        var jpeg = encoded.ToArray();
        var payload = new byte[]
        {
            0x45, 0x78, 0x69, 0x66, 0, 0, 0x49, 0x49, 0x2A, 0, 8, 0, 0, 0,
            1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0,
            (byte)(int)origin, 0, 0, 0, 0, 0, 0, 0
        };
        var segment = new byte[payload.Length + 4];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        var length = payload.Length + 2;
        segment[2] = (byte)(length >> 8);
        segment[3] = (byte)length;
        payload.CopyTo(segment, 4);
        var insertAt = jpeg.Length >= 4 && jpeg[0] == 0xff && jpeg[1] == 0xd8 && jpeg[2] == 0xff && jpeg[3] == 0xe0
            ? 4 + ((jpeg[4] << 8) | jpeg[5])
            : 2;
        var result = new byte[jpeg.Length + segment.Length];
        jpeg.AsSpan(0, insertAt).CopyTo(result);
        segment.CopyTo(result, insertAt);
        jpeg.AsSpan(insertAt).CopyTo(result.AsSpan(insertAt + segment.Length));
        return result;
    }

    private static SKEncodedOrigin ReadOrientation(byte[] jpeg)
    {
        using var data = SKData.CreateCopy(jpeg);
        using var codec = SKCodec.Create(data);
        return codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft;
    }

    [Fact]
    public void Exif_orientation_transforms_all_eight_directions()
    {
        SKColor[] source = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow, SKColors.Cyan, SKColors.Magenta];
        var expected = new Dictionary<SKEncodedOrigin, SKColor[]>
        {
            [SKEncodedOrigin.TopLeft] = source,
            [SKEncodedOrigin.TopRight] = [SKColors.Blue, SKColors.Green, SKColors.Red, SKColors.Magenta, SKColors.Cyan, SKColors.Yellow],
            [SKEncodedOrigin.BottomRight] = [SKColors.Magenta, SKColors.Cyan, SKColors.Yellow, SKColors.Blue, SKColors.Green, SKColors.Red],
            [SKEncodedOrigin.BottomLeft] = [SKColors.Yellow, SKColors.Cyan, SKColors.Magenta, SKColors.Red, SKColors.Green, SKColors.Blue],
            [SKEncodedOrigin.LeftTop] = [SKColors.Red, SKColors.Yellow, SKColors.Green, SKColors.Cyan, SKColors.Blue, SKColors.Magenta],
            [SKEncodedOrigin.RightTop] = [SKColors.Yellow, SKColors.Red, SKColors.Cyan, SKColors.Green, SKColors.Magenta, SKColors.Blue],
            [SKEncodedOrigin.RightBottom] = [SKColors.Magenta, SKColors.Blue, SKColors.Cyan, SKColors.Green, SKColors.Yellow, SKColors.Red],
            [SKEncodedOrigin.LeftBottom] = [SKColors.Blue, SKColors.Magenta, SKColors.Green, SKColors.Cyan, SKColors.Red, SKColors.Yellow]
        };
        using var bitmap = new SKBitmap(new SKImageInfo(3, 2));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint())
        {
            for (var i = 0; i < source.Length; i++)
            {
                paint.Color = source[i];
                var x = i % 3;
                var y = i / 3;
                canvas.DrawRect(new SKRect(x, y, x + 1, y + 1), paint);
            }
            canvas.Flush();
        }
        Assert.Equal(source, Enumerable.Range(0, source.Length).Select(i => bitmap.GetPixel(i % 3, i / 3)).ToArray());
        foreach (var (origin, colors) in expected)
        {
            using var transformed = origin == SKEncodedOrigin.TopLeft ? null : MediaService.ApplyOrigin(bitmap, origin);
            var oriented = transformed ?? bitmap;
            var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            Assert.Equal(swapsAxes ? 2 : 3, oriented.Width);
            Assert.Equal(swapsAxes ? 3 : 2, oriented.Height);
            for (var i = 0; i < colors.Length; i++)
                Assert.True(colors[i].Equals(oriented.GetPixel(i % oriented.Width, i / oriented.Width)),
                    $"{origin} pixel {i}: expected {colors[i]}, actual {oriented.GetPixel(i % oriented.Width, i / oriented.Width)}");
        }
    }

    [Fact]
    public void Jpeg_exif_orientation_is_read_for_all_eight_values_and_malformed_data_is_ignored()
    {
        foreach (var origin in Enum.GetValues<SKEncodedOrigin>())
        {
            var jpeg = JpegWithOrientation(10, 20, origin);
            Assert.True(origin.Equals(ReadOrientation(jpeg)),
                $"{origin}: {Convert.ToHexString(jpeg[..Math.Min(96, jpeg.Length)])}");
        }

        var valid = JpegWithOrientation(10, 20, SKEncodedOrigin.RightTop);
        var exifMarker = Array.IndexOf(valid, (byte)0xe1);
        Assert.True(exifMarker > 0);
        var truncated = valid[..exifMarker];
        var invalidLength = (byte[])valid.Clone();
        invalidLength[exifMarker + 2] = 0xff;
        invalidLength[exifMarker + 3] = 0xff;
        Assert.Equal(SKEncodedOrigin.TopLeft, ReadOrientation([]));
        Assert.Equal(SKEncodedOrigin.TopLeft, ReadOrientation(truncated));
        Assert.Equal(SKEncodedOrigin.TopLeft, ReadOrientation(invalidLength));
    }

    [PostgresFact]
    public async Task Rotated_upload_stores_upright_pixels_and_variant_dimensions()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var service = CreateService(admin, directory);
        var bytes = JpegWithOrientation(400, 800, SKEncodedOrigin.RightTop);
        using (var verify = SKData.CreateCopy(bytes))
        {
            Assert.Equal(SKEncodedOrigin.RightTop, ReadOrientation(bytes));
            using var codec = SKCodec.Create(verify);
            Assert.NotNull(codec);
            Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);
        }

        MediaItem uploaded;
        await using (var stream = new MemoryStream(bytes))
            uploaded = await service.UploadAsync(stream, "portrait.jpg");
        Assert.Equal(800, uploaded.Width);
        Assert.Equal(400, uploaded.Height);

        var storage = directory.Create();
        using (var verify = SKData.Create(Path.Combine(storage.Images, uploaded.FileName)))
        using (var codec = SKCodec.Create(verify))
        {
            Assert.NotNull(codec);
            Assert.Equal(SKEncodedOrigin.TopLeft, codec!.EncodedOrigin);
            Assert.Equal(800, codec.Info.Width);
            Assert.Equal(400, codec.Info.Height);
        }
        var variantPath = Path.Combine(storage.Images, MediaService.VariantName(uploaded.FileName, 480));
        Assert.True(File.Exists(variantPath));
        Assert.False(File.Exists(Path.Combine(storage.Images, MediaService.VariantName(uploaded.FileName, 960))));
        using (var verify = SKData.Create(variantPath))
        using (var codec = SKCodec.Create(verify))
        {
            Assert.NotNull(codec);
            Assert.Equal(480, codec!.Info.Width);
            Assert.Equal(240, codec.Info.Height);
            Assert.Equal(MediaService.WebpSupported ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        }
    }

    private static MediaService CreateService(TestActor actor, TemporaryMediaStorage directory) => new(
        actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
        actor.Services.GetRequiredService<AdminAccess>(), directory.Create(), TimeProvider.System, NullLogger<MediaService>.Instance,
        actor.Services.GetRequiredService<IHttpClientFactory>(),
        actor.Services.GetRequiredService<StorageMutationCoordinator>());

    [PostgresFact]
    public async Task Locked_variant_restores_the_original_and_keeps_the_media_row()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var service = CreateService(admin, directory);
        MediaItem uploaded;
        await using (var stream = new MemoryStream(Png(1200, 400)))
            uploaded = await service.UploadAsync(stream, "locked-delete.png");
        var storage = directory.Create();
        var original = Path.Combine(storage.Images, uploaded.FileName);
        var variant = Path.Combine(storage.Images, MediaService.VariantName(uploaded.FileName, 480));
        var originalBytes = await File.ReadAllBytesAsync(original);
        using var locked = new FileStream(variant, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var refused = await Assert.ThrowsAsync<CmsException>(() => service.DeleteAsync(uploaded.Id, true));
        Assert.Contains("媒體資料已保留", refused.Message, StringComparison.Ordinal);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(original));
        Assert.True(File.Exists(variant));
        await using var db = await App.CreateContextAsync();
        Assert.True(await db.MediaAssets.AnyAsync(x => x.Id == uploaded.Id));
    }

    [PostgresFact]
    public async Task Storage_mutations_are_serialized_across_request_scopes()
    {
        using var firstAdmin = await App.AdministratorAsync();
        using var secondAdmin = await App.AdministratorAsync();
        var first = firstAdmin.Services.GetRequiredService<StorageMutationCoordinator>();
        var second = secondAdmin.Services.GetRequiredService<StorageMutationCoordinator>();
        var firstLease = await first.AcquireAsync(SiteDefaults.DefaultSiteId);
        try
        {
            var waiting = second.AcquireAsync(SiteDefaults.DefaultSiteId);
            await Assert.ThrowsAsync<TimeoutException>(() => waiting.WaitAsync(TimeSpan.FromMilliseconds(250)));
            await firstLease.DisposeAsync();
            await using var secondLease = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await firstLease.DisposeAsync();
        }
    }

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
        // 變體副檔名由 MediaService.VariantName 決定（WebP 編碼器可用時為 .webp）。
        Assert.True(File.Exists(Path.Combine(images, MediaService.VariantName(wide.FileName, 480))));
        Assert.True(File.Exists(Path.Combine(images, MediaService.VariantName(wide.FileName, 960))));
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

    [PostgresTheory]
    [InlineData(CustomFieldTypes.Media)]
    [InlineData(CustomFieldTypes.Gallery)]
    public async Task Custom_field_references_refuse_media_deletion_until_removed(string fieldType)
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var service = CreateService(admin, directory);
        var asset = new MediaAsset
        {
            FileName = Guid.NewGuid().ToString("N") + ".png", OriginalName = "field-reference.png",
            ContentType = "image/png", Size = 1, UploadedAt = DateTimeOffset.UtcNow
        };
        await using (var db = await App.CreateContextAsync())
        {
            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync();
        }
        Assert.True(ContentRules.IsSafeMediaFileNameAny(asset.FileName));
        var field = await admin.Services.GetRequiredService<ContentTypeService>().SaveFieldAsync(new ContentTypeService.FieldEdit
        {
            BuiltinKind = ContentKind.Post, Name = "欄位媒體", Key = "field-media", FieldType = fieldType
        });
        var post = NewContent("自訂欄位引用媒體");
        post.FieldValues = [new(field.Id, "/media/" + asset.FileName)];
        var saved = await admin.Content.SaveAsync(post);

        Assert.Equal(1, await service.CountReferencesAsync(asset.Id));
        Assert.Equal(saved.Id, Assert.Single(await service.ListReferencesAsync(asset.Id)).ContentId);
        var refused = await Assert.ThrowsAsync<CmsException>(() => service.DeleteAsync(asset.Id, confirmed: true));
        Assert.Contains("自訂欄位引用媒體", refused.Message, StringComparison.Ordinal);
        await using (var db = await App.CreateContextAsync())
            Assert.True(await db.MediaAssets.AnyAsync(x => x.Id == asset.Id));

        var edit = await admin.Content.GetEditAsync(saved.Id!.Value);
        edit.FieldValues = [];
        await admin.Content.UpdateAsync(edit);
        Assert.Equal(0, await service.CountReferencesAsync(asset.Id));
        await service.DeleteAsync(asset.Id, confirmed: true);
        await using var verify = await App.CreateContextAsync();
        Assert.False(await verify.MediaAssets.AnyAsync(x => x.Id == asset.Id));
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

    [PostgresFact]
    public async Task Widget_media_references_block_deletion_until_the_widget_is_removed()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var service = CreateService(admin, directory);
        MediaItem uploaded;
        await using (var stream = new MemoryStream(Png(640, 320)))
            uploaded = await service.UploadAsync(stream, "widget-reference.png");
        var widgets = admin.Services.GetRequiredService<WidgetService>();
        var widget = await widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Sidebar,
            Type = WidgetType.Image,
            Title = "側欄圖片",
            Url = "/media/" + uploaded.FileName,
            IsVisible = false
        });

        Assert.Equal(1, await service.CountReferencesAsync(uploaded.Id));
        Assert.Equal(1, (await service.ReferenceCountsAsync([uploaded.Id]))[uploaded.Id]);
        var reference = Assert.Single(await service.ListReferencesAsync(uploaded.Id));
        Assert.Equal(widget.Id, reference.WidgetId);
        Assert.Null(reference.Kind);
        Assert.Null(reference.Status);
        var refused = await Assert.ThrowsAsync<CmsException>(() => service.DeleteAsync(uploaded.Id, true));
        Assert.Contains("側欄圖片", refused.Message, StringComparison.Ordinal);

        await widgets.DeleteAsync(widget.Id, widget.Version);
        Assert.Equal(0, await service.CountReferencesAsync(uploaded.Id));
        await service.DeleteAsync(uploaded.Id, true);
    }

    [PostgresFact]
    public async Task Content_save_holds_the_site_storage_mutation_lock_until_commit()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var gate = new SaveGate();
        var service = new ContentService(
            new InterceptingContextFactory(App.ConnectionString, gate),
            admin.Services.GetRequiredService<AdminAccess>(),
            TimeProvider.System,
            storageMutations: admin.Services.GetRequiredService<StorageMutationCoordinator>());
        var edit = NewContent("儲存鎖文章");
        edit.Markdown = "儲存期間持有站點儲存鎖。";
        var coordinator = admin.Services.GetRequiredService<StorageMutationCoordinator>();
        Task<ContentEdit>? saving = null;
        Task<IAsyncDisposable>? waiting = null;
        try
        {
            saving = service.SaveAsync(edit);
            await gate.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            waiting = coordinator.AcquireAsync(SiteDefaults.DefaultSiteId);
            await Assert.ThrowsAsync<TimeoutException>(() => waiting.WaitAsync(TimeSpan.FromMilliseconds(250)));
            gate.Release.TrySetResult();
            var saved = await saving.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(edit.Title, saved.Title);
            await using var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(lease);
        }
        finally
        {
            gate.Release.TrySetResult();
            if (saving is not null) await ObserveAsync(saving);
            if (waiting is not null)
            {
                try { await using var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch { /* 測試失敗時仍需觀察並釋放已完成的等候工作。 */ }
            }
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { /* 清理時保留原始測試失敗。 */ }
    }

    private sealed class InterceptingContextFactory(string connectionString, IInterceptor interceptor) : IDbContextFactory<CmsDbContext>
    {
        public CmsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CmsDbContext>().UseNpgsql(connectionString, o => o.UseVector())
            .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .AddInterceptors(interceptor).Options);

        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class SaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Ready.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }
}
