namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MediaServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresTheory]
    [InlineData(SKEncodedImageFormat.Png, "照片.PNG", ".png", "image/png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "photo.jpg", ".jpg", "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Jpeg, "photo.JPEG", ".jpg", "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "photo.WeBp", ".webp", "image/webp")]
    public async Task Upload_reencodes_decodable_images_and_persists_metadata_across_service_recreation(
        SKEncodedImageFormat format, string name, string extension, string contentType)
    {
        using var directory = new TemporaryMediaStorage();
        var storage = directory.Create();
        using var actor = await App.AdministratorAsync();
        var service = CreateService(actor, storage);
        var marker = Encoding.ASCII.GetBytes("untrusted-original-trailer-must-not-be-published");
        var original = EncodeImage(format).Concat(marker).ToArray();
        AssertDecodable(original, format, 64, 48);
        using var stream = new MemoryStream(original);
        var before = DateTimeOffset.UtcNow;
        var item = await service.UploadAsync(stream, name);
        Assert.True(stream.CanRead); // Ownership of the caller's stream is not transferred.
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(item.Id.ToString("N") + extension, item.FileName);
        Assert.Equal(name, item.OriginalName);
        Assert.Equal(contentType, item.ContentType);
        Assert.Equal(64, item.Width);
        Assert.Equal(48, item.Height);
        Assert.Equal(TimeSpan.Zero, item.UploadedAt.Offset);
        Assert.InRange(item.UploadedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal("/media/" + item.FileName, item.Url);
        Assert.Contains(item.Url, item.Markdown, StringComparison.Ordinal);
        var path = Path.Combine(storage.Images, item.FileName);
        var published = await File.ReadAllBytesAsync(path);
        Assert.Equal(published.LongLength, item.Size);
        Assert.InRange(item.Size, 1, Limits.UploadBytes);
        Assert.False(original.SequenceEqual(published));
        Assert.Equal(-1, published.AsSpan().IndexOf(marker));
        AssertDecodable(published, format, item.Width, item.Height);
        Assert.Single(Directory.GetFiles(storage.Images));
        Assert.Empty(Directory.GetFiles(storage.Staging));
        Assert.Empty(Directory.GetFiles(storage.Keys));

        var persisted = Assert.Single(await ReadAssetsAsync());
        Assert.Equal(DatabasePrecision(item), persisted);
        var recreatedStorage = directory.Create();
        using var freshActor = await App.AdministratorAsync();
        var recreatedService = CreateService(freshActor, recreatedStorage);
        Assert.Equal(persisted, Assert.Single((await recreatedService.ListAsync()).Items));
        Assert.Equal(published, await File.ReadAllBytesAsync(Path.Combine(recreatedStorage.Images, persisted.FileName)));
        using var duplicateSource = new MemoryStream(original);
        var second = await recreatedService.UploadAsync(duplicateSource, name);
        Assert.NotEqual(item.Id, second.Id);
        Assert.NotEqual(item.FileName, second.FileName);
        Assert.Equal(2, (await recreatedService.ListAsync()).Total);
        Assert.Equal(2, Directory.GetFiles(storage.Images).Length);
        Assert.Equal(published, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(storage.Staging));
    }

    [PostgresFact]
    public async Task Media_search_matches_original_names_and_alt_text()
    {
        using var directory = new TemporaryMediaStorage();
        using var actor = await App.AdministratorAsync();
        var service = CreateService(actor, directory.Create());
        using (var stream = new MemoryStream(EncodeImage(SKEncodedImageFormat.Png)))
        {
            await service.UploadAsync(stream, "tokyo-skyline.png");
        }
        MediaItem described;
        using (var stream = new MemoryStream(EncodeImage(SKEncodedImageFormat.Png)))
        {
            described = await service.UploadAsync(stream, "random-name.png");
        }
        described = await service.UpdateAltAsync(described.Id, "京都的街道");

        Assert.Single((await service.ListAsync(search: "skyline")).Items);
        Assert.Single((await service.ListAsync(search: "SKYLINE")).Items);
        Assert.Single((await service.ListAsync(search: "京都")).Items);
        Assert.Empty((await service.ListAsync(search: "大阪")).Items);
    }

    [PostgresFact]
    public async Task Image_list_pages_by_timestamp_then_id_without_duplicates_or_missing_metadata()
    {
        using var directory = new TemporaryMediaStorage();
        using var actor = await App.AdministratorAsync();
        var service = CreateService(actor, directory.Create());
        var items = new List<MediaItem>();
        var timestamp = new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero);
        for (var i = 0; i < 7; i++)
        {
            using var stream = new MemoryStream(EncodeImage(SKEncodedImageFormat.Png));
            items.Add((await service.UploadAsync(stream, $"image-{i}.png")) with
            {
                UploadedAt = i < 5 ? timestamp : timestamp.AddDays(1)
            });
        }
        await using (var db = await App.CreateContextAsync())
        {
            foreach (var row in await db.MediaAssets.ToListAsync())
                row.UploadedAt = items.Single(x => x.Id == row.Id).UploadedAt;
            await db.SaveChangesAsync();
        }
        var expected = items.OrderByDescending(x => x.UploadedAt)
            .ThenByDescending(x => x.Id.ToString("N"), StringComparer.Ordinal).ToArray();
        var actual = new List<MediaItem>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await service.ListAsync(page, 3);
            Assert.Equal(7, result.Total);
            Assert.Equal(3, result.TotalPages);
            Assert.Equal(page, result.Page);
            Assert.Equal(3, result.PageSize);
            Assert.Equal(page == 3 ? 1 : 3, result.Items.Count);
            Assert.Equal(result.Items, (await service.ListAsync(page, 3)).Items);
            actual.AddRange(result.Items);
        }
        Assert.Equal(expected, actual);
        Assert.Equal(7, actual.Select(x => x.Id).Distinct().Count());
        var beyond = await service.ListAsync(4, 3);
        Assert.Empty(beyond.Items);
        Assert.Equal(7, beyond.Total);
    }

    [PostgresFact]
    public async Task Image_list_defaults_empty_pages_and_parameter_limits_are_enforced()
    {
        using var directory = new TemporaryMediaStorage();
        using var actor = await App.AdministratorAsync();
        var storage = directory.Create();
        var service = CreateService(actor, storage);
        var empty = await service.ListAsync();
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.Total);
        Assert.Equal(1, empty.Page);
        Assert.Equal(1, empty.TotalPages);
        Assert.Equal(Limits.PageSize, empty.PageSize);
        Assert.Empty((await service.ListAsync(1, Limits.MaxPageSize)).Items);
        var before = await SnapshotAsync(storage);
        foreach (var (page, size) in new[] { (0, 10), (-1, 10), (1, 0), (1, -1), (1, Limits.MaxPageSize + 1), (int.MaxValue, 2) })
            await Assert.ThrowsAsync<CmsException>(() => service.ListAsync(page, size));
        await AssertUnchangedAsync(storage, before);
    }

    [PostgresTheory]
    [InlineData(SKEncodedImageFormat.Png, "spoof.jpg")]
    [InlineData(SKEncodedImageFormat.Png, "spoof.webp")]
    [InlineData(SKEncodedImageFormat.Jpeg, "spoof.png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "spoof.webp")]
    [InlineData(SKEncodedImageFormat.Webp, "spoof.png")]
    [InlineData(SKEncodedImageFormat.Webp, "spoof.jpeg")]
    [InlineData(SKEncodedImageFormat.Png, "image.svg")]
    [InlineData(SKEncodedImageFormat.Png, "image.exe")]
    [InlineData(SKEncodedImageFormat.Png, "image")]
    [InlineData(SKEncodedImageFormat.Png, "image.png.exe")]
    public Task Actual_format_must_match_an_allowed_extension(SKEncodedImageFormat format, string name) =>
        AssertRejectedAsync(EncodeImage(format), name);

    [PostgresTheory]
    [InlineData("svg", "image.svg")]
    [InlineData("svg", "image.png")]
    [InlineData("svg", "image.jpg")]
    [InlineData("svg", "image.webp")]
    [InlineData("executable", "program.png")]
    [InlineData("executable", "program.jpg")]
    [InlineData("executable", "program.webp")]
    [InlineData("empty", "empty.png")]
    public Task Non_raster_executable_and_empty_inputs_leave_no_files_or_metadata(string payload, string name)
    {
        var bytes = payload switch
        {
            "svg" => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\"/></svg>"),
            "executable" => Encoding.ASCII.GetBytes("MZ\0\0This program cannot be run in DOS mode.\0PE\0\0"),
            "empty" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(payload))
        };
        return AssertRejectedAsync(bytes, name);
    }

    [PostgresTheory]
    [InlineData(SKEncodedImageFormat.Png, "truncated.png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "truncated.jpg")]
    [InlineData(SKEncodedImageFormat.Webp, "truncated.webp")]
    public async Task Truncated_encoded_images_are_rejected(SKEncodedImageFormat format, string name)
    {
        var complete = EncodeImage(format);
        AssertDecodable(complete, format, 64, 48);
        await AssertRejectedAsync(complete[..(complete.Length / 2)], name);
    }

    public static IEnumerable<object?[]> UnsafeNames()
    {
        foreach (var name in new[]
        {
            null, "", " ", ".", "..", "../photo.png", "..\\photo.png", "nested/photo.png", "nested\\photo.png",
            "/photo.png", "\\photo.png", "C:\\photo.png", "C:photo.png", "photo.png:stream", "\\\\server\\photo.png",
            "nul\0.png", "line\nbreak.png", "return\r.png", "tab\t.png", "delete\u007f.png", new string('a', 197) + ".png"
        }) yield return [name];
    }

    [PostgresTheory]
    [MemberData(nameof(UnsafeNames))]
    public Task Unsafe_original_names_are_rejected_without_using_them_as_paths(string? name) =>
        AssertRejectedAsync(EncodeImage(SKEncodedImageFormat.Png), name!);

    [PostgresFact]
    public async Task Maximum_length_original_name_is_metadata_only()
    {
        using var directory = new TemporaryMediaStorage();
        using var actor = await App.AdministratorAsync();
        var storage = directory.Create();
        var name = new string('a', 196) + ".png";
        using var stream = new MemoryStream(EncodeImage(SKEncodedImageFormat.Png));
        var item = await CreateService(actor, storage).UploadAsync(stream, name);
        Assert.Equal(name, item.OriginalName);
        Assert.Equal(200, item.OriginalName.Length);
        Assert.Equal(item.Id.ToString("N") + ".png", item.FileName);
        Assert.Equal(DatabasePrecision(item), Assert.Single(await ReadAssetsAsync()));
        Assert.Single(Directory.GetFiles(storage.Images));
        Assert.Empty(Directory.GetFiles(storage.Staging));
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exactly_five_MiB_is_accepted_even_when_stream_length_is_unavailable(bool nonseekable)
    {
        using var directory = new TemporaryMediaStorage();
        using var actor = await App.AdministratorAsync();
        var storage = directory.Create();
        var bytes = PaddedPng(checked((int)Limits.UploadBytes));
        using Stream stream = nonseekable ? new NonSeekableReadStream(bytes) : new MemoryStream(bytes);
        Assert.Equal(!nonseekable, stream.CanSeek);
        var item = await CreateService(actor, storage).UploadAsync(stream, "limit.png");
        Assert.True(item.Size < Limits.UploadBytes);
        AssertDecodable(await File.ReadAllBytesAsync(Path.Combine(storage.Images, item.FileName)), SKEncodedImageFormat.Png, 64, 48);
        Assert.Equal(DatabasePrecision(item), Assert.Single(await ReadAssetsAsync()));
        Assert.Single(Directory.GetFiles(storage.Images));
        Assert.Empty(Directory.GetFiles(storage.Staging));
    }

    [PostgresTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(true, 1_000_000)]
    public async Task Size_limit_counts_bytes_read_and_stops_oversized_nonseekable_streams(bool nonseekable, int excess)
    {
        using var directory = new TemporaryMediaStorage();
        using var actor = await App.AdministratorAsync();
        var storage = directory.Create();
        var service = CreateService(actor, storage);
        await SeedAsync(service);
        var before = await SnapshotAsync(storage);
        var bytes = PaddedPng(checked((int)Limits.UploadBytes + excess));
        using Stream stream = nonseekable ? new NonSeekableReadStream(bytes) : new MemoryStream(bytes);
        await Assert.ThrowsAsync<CmsException>(() => service.UploadAsync(stream, "oversized.png"));
        var read = stream is NonSeekableReadStream tracking ? tracking.BytesRead : stream.Position;
        Assert.InRange(read, Limits.UploadBytes + 1, Limits.UploadBytes + 81920);
        if (excess > 81920) Assert.True(read < bytes.LongLength);
        Assert.True(stream.CanRead);
        await AssertUnchangedAsync(storage, before);
    }

    [PostgresTheory]
    [InlineData(10_001, 1)]
    [InlineData(1, 10_001)]
    [InlineData(5_000, 4_001)]
    public async Task Dimensions_and_total_pixel_limit_are_checked_on_compressed_images(int width, int height)
    {
        var bytes = EncodeImage(SKEncodedImageFormat.Png, width, height);
        Assert.True(bytes.LongLength < Limits.UploadBytes);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(width, codec.Info.Width);
        Assert.Equal(height, codec.Info.Height);
        await AssertRejectedAsync(bytes, "oversized-dimensions.png");
    }

    [PostgresTheory]
    [InlineData(10_000, 1)]
    [InlineData(1, 10_000)]
    [InlineData(5_000, 4_000)]
    public async Task Exact_dimension_and_pixel_limits_remain_accepted(int width, int height)
    {
        using var directory = new TemporaryMediaStorage();
        using var actor = await App.AdministratorAsync();
        var storage = directory.Create();
        using var stream = new MemoryStream(EncodeImage(SKEncodedImageFormat.Png, width, height));
        var item = await CreateService(actor, storage).UploadAsync(stream, "boundary.png");
        Assert.Equal(width, item.Width);
        Assert.Equal(height, item.Height);
        AssertDecodable(await File.ReadAllBytesAsync(Path.Combine(storage.Images, item.FileName)), SKEncodedImageFormat.Png, width, height);
        Assert.Equal(DatabasePrecision(item), Assert.Single(await ReadAssetsAsync()));
        Assert.Empty(Directory.GetFiles(storage.Staging));
    }

    [PostgresFact]
    public async Task Animated_WebP_is_rejected_even_though_its_format_and_dimensions_are_allowed()
    {
        var bytes = AnimatedWebp();
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal(2, codec.FrameCount);
        Assert.Equal(64, codec.Info.Width);
        Assert.Equal(48, codec.Info.Height);
        using var bitmap = new SKBitmap(codec.Info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(bitmap.Info, bitmap.GetPixels()));
        await AssertRejectedAsync(bytes, "animated.webp");
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anonymous_and_expired_stamp_cannot_list_or_upload_and_do_not_read_the_stream(bool expiredStamp)
    {
        using var directory = new TemporaryMediaStorage();
        var storage = directory.Create();
        using var admin = await App.AdministratorAsync();
        await SeedAsync(CreateService(admin, storage));
        using var actor = expiredStamp ? await App.AdministratorAsync() : App.Anonymous();
        var service = CreateService(actor, storage);
        if (expiredStamp) await RotateStampAsync();
        var before = await SnapshotAsync(storage);
        using var stream = new NonSeekableReadStream(EncodeImage(SKEncodedImageFormat.Png));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UploadAsync(stream, "denied.png"));
        Assert.Equal(0, stream.ReadCalls);
        await AssertUnchangedAsync(storage, before);
        using var fresh = await App.AdministratorAsync();
        Assert.Single((await CreateService(fresh, storage).ListAsync()).Items);
    }

    [PostgresFact]
    public async Task Stamp_revoked_while_reading_is_rechecked_before_publication_and_staged_file_is_removed()
    {
        using var directory = new TemporaryMediaStorage();
        var storage = directory.Create();
        using var actor = await App.AdministratorAsync();
        var service = CreateService(actor, storage);
        await SeedAsync(service);
        var before = await SnapshotAsync(storage);
        var bytes = EncodeImage(SKEncodedImageFormat.Png);
        using var stream = new NonSeekableReadStream(bytes, async count =>
        {
            if (count == 0) await RotateStampAsync();
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UploadAsync(stream, "revoked.png"));
        Assert.Equal(bytes.LongLength, stream.BytesRead);
        await AssertUnchangedAsync(storage, before);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_before_or_during_read_preserves_existing_files_and_metadata(bool duringRead)
    {
        using var directory = new TemporaryMediaStorage();
        var storage = directory.Create();
        using var actor = await App.AdministratorAsync();
        var service = CreateService(actor, storage);
        await SeedAsync(service);
        var before = await SnapshotAsync(storage);
        using var cancellation = new CancellationTokenSource();
        using var stream = new NonSeekableReadStream(EncodeImage(SKEncodedImageFormat.Png), _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        if (!duringRead) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UploadAsync(stream, "cancelled.png", cancellation.Token));
        Assert.Equal(duringRead ? 1 : 0, stream.ReadCalls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ListAsync(ct: cancellation.Token));
        await AssertUnchangedAsync(storage, before);
        // A failed operation must release the decode slot and leave the service usable.
        await SeedAsync(service);
        Assert.Equal(2, (await service.ListAsync()).Total);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_failure_or_cancellation_after_public_move_rolls_back_new_file_and_metadata(bool cancel)
    {
        using var directory = new TemporaryMediaStorage();
        var storage = directory.Create();
        using var actor = await App.AdministratorAsync();
        var existing = await SeedAsync(CreateService(actor, storage));
        var before = await SnapshotAsync(storage);
        using var cancellation = new CancellationTokenSource();
        var reachedDatabaseWrite = false;
        var factory = new CallbackContextFactory(Factory, db =>
        {
            // Only the media write uses this factory; AdminAccess still uses the App factory.
            Assert.Equal(before.Images.Length + 1, Directory.GetFiles(storage.Images).Length);
            Assert.Empty(Directory.GetFiles(storage.Staging));
            reachedDatabaseWrite = true;
            if (cancel) cancellation.Cancel();
            else
            {
                // Exercise a real PostgreSQL primary-key violation, not an in-memory provider.
                db.MediaAssets.Add(new MediaAsset
                {
                    Id = existing.Id, FileName = existing.FileName, OriginalName = existing.OriginalName,
                    ContentType = existing.ContentType, Size = existing.Size, Width = existing.Width,
                    Height = existing.Height, UploadedAt = existing.UploadedAt
                });
            }
        });
        var service = CreateService(actor, storage, factory);
        using var stream = new MemoryStream(EncodeImage(SKEncodedImageFormat.Png));
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UploadAsync(stream, "cancel-at-save.png", cancellation.Token));
        else
            await Assert.ThrowsAsync<CmsException>(() => service.UploadAsync(stream, "database-failure.png"));
        Assert.True(reachedDatabaseWrite);
        await AssertUnchangedAsync(storage, before);
    }

    private IDbContextFactory<CmsDbContext> Factory => App.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>();

    private MediaService CreateService(TestActor actor, StoragePaths storage, IDbContextFactory<CmsDbContext>? factory = null) =>
        new(factory ?? Factory, actor.Services.GetRequiredService<AdminAccess>(), storage,
            TimeProvider.System, NullLogger<MediaService>.Instance);

    private async Task RotateStampAsync()
    {
        using var revoker = App.Anonymous();
        var user = await revoker.Users.Users.SingleAsync();
        Assert.True((await revoker.Users.UpdateSecurityStampAsync(user)).Succeeded);
    }

    private async Task AssertRejectedAsync(byte[] bytes, string name)
    {
        using var directory = new TemporaryMediaStorage();
        var storage = directory.Create();
        using var actor = await App.AdministratorAsync();
        var service = CreateService(actor, storage);
        await SeedAsync(service);
        var before = await SnapshotAsync(storage);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<CmsException>(() => service.UploadAsync(stream, name));
        await AssertUnchangedAsync(storage, before);
    }

    private static async Task<MediaItem> SeedAsync(MediaService service)
    {
        using var stream = new MemoryStream(EncodeImage(SKEncodedImageFormat.Png));
        return await service.UploadAsync(stream, "existing.png");
    }

    private async Task<MediaItem[]> ReadAssetsAsync()
    {
        await using var db = await App.CreateContextAsync();
        return await db.MediaAssets.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new MediaItem(x.Id, x.FileName, x.OriginalName, x.ContentType, x.Size, x.Width, x.Height, x.UploadedAt))
            .ToArrayAsync();
    }

    private sealed record StorageSnapshot(string[] Images, string[] Staging, string[] Files, string[] Hashes, MediaItem[] Assets);

    private async Task<StorageSnapshot> SnapshotAsync(StoragePaths storage)
    {
        var images = Directory.GetFiles(storage.Images, "*", SearchOption.AllDirectories).Order().ToArray();
        var staging = Directory.GetFiles(storage.Staging, "*", SearchOption.AllDirectories).Order().ToArray();
        var files = Directory.GetFiles(storage.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var hashes = files.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
        return new(images, staging, files, hashes, await ReadAssetsAsync());
    }

    private async Task AssertUnchangedAsync(StoragePaths storage, StorageSnapshot before)
    {
        var after = await SnapshotAsync(storage);
        Assert.Equal(before.Images.Length, after.Images.Length);
        Assert.Equal(before.Staging.Length, after.Staging.Length);
        Assert.Equal(before.Files.Length, after.Files.Length);
        Assert.Equal(before.Images, after.Images);
        Assert.Equal(before.Staging, after.Staging);
        Assert.Equal(before.Files, after.Files);
        Assert.Equal(before.Hashes, after.Hashes);
        Assert.Equal(before.Assets, after.Assets);
    }

    // PostgreSQL timestamps have microsecond precision; TimeProvider.System may provide 100 ns ticks.
    private static MediaItem DatabasePrecision(MediaItem item) => item with
    {
        UploadedAt = new DateTimeOffset(item.UploadedAt.Ticks - item.UploadedAt.Ticks % 10, TimeSpan.Zero)
    };

    private static byte[] EncodeImage(SKEncodedImageFormat format, int width = 64, int height = 48, bool alternate = false)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(alternate ? SKColors.Coral : SKColors.Teal);
        using var paint = new SKPaint { Color = SKColors.Gold };
        canvas.DrawRect(new SKRect(width / 4f, height / 4f, width * 3 / 4f, height * 3 / 4f), paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 83);
        Assert.NotNull(data);
        return data.ToArray();
    }

    private static byte[] PaddedPng(int length)
    {
        var bytes = new byte[length];
        EncodeImage(SKEncodedImageFormat.Png).CopyTo(bytes, 0);
        return bytes;
    }

    private static void AssertDecodable(byte[] bytes, SKEncodedImageFormat format, int width, int height)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(format, codec.EncodedFormat);
        Assert.Equal(width, codec.Info.Width);
        Assert.Equal(height, codec.Info.Height);
        using var bitmap = new SKBitmap(codec.Info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(bitmap.Info, bitmap.GetPixels()));
        Assert.NotEqual(bitmap.GetPixel(0, 0), bitmap.GetPixel(width / 2, height / 2));
    }

    private static byte[] AnimatedWebp()
    {
        // Skia encodes the two still frames; the small RIFF envelope supplies animation metadata.
        using var chunks = new MemoryStream();
        using (var writer = new BinaryWriter(chunks, Encoding.ASCII, leaveOpen: true))
        {
            WriteChunk(writer, "VP8X", [2, 0, 0, 0, 63, 0, 0, 47, 0, 0]);
            WriteChunk(writer, "ANIM", new byte[6]);
            for (var frame = 0; frame < 2; frame++)
            {
                var still = EncodeImage(SKEncodedImageFormat.Webp, alternate: frame == 1);
                Assert.Equal("RIFF", Encoding.ASCII.GetString(still, 0, 4));
                Assert.Equal("WEBP", Encoding.ASCII.GetString(still, 8, 4));
                Assert.Equal("VP8 ", Encoding.ASCII.GetString(still, 12, 4));
                using var payload = new MemoryStream();
                // x/y = 0, width/height minus one, 100 ms duration, no blending.
                payload.Write(new byte[] { 0, 0, 0, 0, 0, 0, 63, 0, 0, 47, 0, 0, 100, 0, 0, 2 });
                payload.Write(still.AsSpan(12));
                WriteChunk(writer, "ANMF", payload.ToArray());
            }
        }
        using var result = new MemoryStream();
        using (var writer = new BinaryWriter(result, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(checked((uint)chunks.Length + 4));
            writer.Write(Encoding.ASCII.GetBytes("WEBP"));
            writer.Write(chunks.ToArray());
        }
        return result.ToArray();
    }

    private static void WriteChunk(BinaryWriter writer, string name, byte[] payload)
    {
        writer.Write(Encoding.ASCII.GetBytes(name));
        writer.Write((uint)payload.Length);
        writer.Write(payload);
        if (payload.Length % 2 != 0) writer.Write((byte)0);
    }

    private sealed class CallbackContextFactory(IDbContextFactory<CmsDbContext> inner, Action<CmsDbContext> onCreate)
        : IDbContextFactory<CmsDbContext>
    {
        public CmsDbContext CreateDbContext() => throw new NotSupportedException();

        public async Task<CmsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var db = await inner.CreateDbContextAsync(cancellationToken);
            try { onCreate(db); return db; }
            catch { await db.DisposeAsync(); throw; }
        }
    }

    private sealed class NonSeekableReadStream(byte[] bytes, Func<int, Task>? afterRead = null) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false);
        public long BytesRead { get; private set; }
        public int ReadCalls { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await inner.ReadAsync(buffer, cancellationToken);
            ReadCalls++;
            BytesRead += count;
            if (afterRead is not null) await afterRead(count);
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
