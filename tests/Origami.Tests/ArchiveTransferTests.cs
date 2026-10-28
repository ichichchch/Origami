namespace Origami.Tests;

/// <summary>完整備份封裝：匯出打包媒體、ZIP 匯入還原資料與圖片檔案、缺檔整體拒絕。</summary>
[Collection(PostgresCollection.Name)]
public sealed class ArchiveTransferTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Token = "isolated-test-service-token-0123456789abcdef";

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        bitmap.Erase(SKColors.SeaGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90)!;
        return encoded.ToArray();
    }

    private static async Task<byte[]> ArchiveOfAsync(TestActor actor)
    {
        var export = actor.Services.GetRequiredService<ExportService>();
        using var buffer = new MemoryStream();
        await export.WriteArchiveAsync(buffer);
        return buffer.ToArray();
    }

    [PostgresFact]
    public async Task Archive_export_packs_data_and_media_and_zip_import_restores_everything()
    {
        using var admin = await App.AdministratorAsync();
        var media = new MediaService(admin.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            admin.Services.GetRequiredService<AdminAccess>(), App.Storage, TimeProvider.System, NullLogger<MediaService>.Instance);
        MediaItem image;
        await using (var stream = new MemoryStream(Png(1200, 400)))
        {
            image = await media.UploadAsync(stream, "cover.png");
        }
        await admin.Content.SaveAsync(NewContent("封裝測試文章", status: ContentStatus.Published));
        var stem = Path.GetFileNameWithoutExtension(image.FileName);
        // 放一個不在媒體庫的孤兒檔；完整還原後應被清理。
        var orphan = Path.Combine(App.Storage.Images, "orphan-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(orphan, Png(10, 10));

        var archive = await ArchiveOfAsync(admin);
        using (var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read))
        {
            Assert.NotNull(zip.GetEntry("origami-export.json"));
            Assert.NotNull(zip.GetEntry("media/" + image.FileName));
            Assert.NotNull(zip.GetEntry("media/" + stem + "_w480.png"));
        }

        // 模擬災損：清掉圖片檔案後以封裝還原（資料面為取代式匯入）。
        foreach (var path in Directory.EnumerateFiles(App.Storage.Images)) File.Delete(path);
        using var restore = new MemoryStream(archive);
        var preview = await admin.Services.GetRequiredService<ImportService>().ApplyAsync(restore, confirmed: true);

        Assert.Equal(1, preview.Media);
        var posts = await admin.Content.ListAdminAsync(ContentKind.Post);
        Assert.Equal("封裝測試文章", posts.Items.Single().Title);
        Assert.True(File.Exists(Path.Combine(App.Storage.Images, image.FileName)), "原圖隨封裝還原");
        Assert.True(File.Exists(Path.Combine(App.Storage.Images, stem + "_w480.png")), "變體隨封裝還原");
        Assert.False(File.Exists(orphan), "清單外的孤兒檔在完整還原後被清理");
    }

    [PostgresFact]
    public async Task Zip_missing_required_media_is_rejected_before_any_data_changes()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("既有內容"));
        var media = new MediaService(admin.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            admin.Services.GetRequiredService<AdminAccess>(), App.Storage, TimeProvider.System, NullLogger<MediaService>.Instance);
        await using (var stream = new MemoryStream(Png(900, 300)))
        {
            await media.UploadAsync(stream, "must-ship.png");
        }

        // 只含 JSON、不含 media/ 檔案的封裝：缺檔必須在動資料庫前整體拒絕。
        using var jsonOnly = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(jsonOnly);
        var container = new MemoryStream();
        using (var archive = new ZipArchive(container, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("origami-export.json");
            await using (var target = entry.Open())
            {
                jsonOnly.Position = 0;
                await jsonOnly.CopyToAsync(target);
            }
        }
        var bytes = container.ToArray();

        using var payload = new MemoryStream(bytes);
        var refused = await Assert.ThrowsAsync<CmsException>(() =>
            admin.Services.GetRequiredService<ImportService>().ApplyAsync(payload, confirmed: true));
        Assert.Contains("缺少媒體檔案", refused.Message, StringComparison.Ordinal);
        var posts = await admin.Content.ListAdminAsync(ContentKind.Post);
        Assert.Equal("既有內容", posts.Items.Single().Title);
    }

    [PostgresFact]
    public async Task Analyze_accepts_both_json_and_zip_containers()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("分析測試"));
        using var json = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(json);
        var import = admin.Services.GetRequiredService<ImportService>();

        json.Position = 0;
        var fromJson = await import.AnalyzeAsync(json);

        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("origami-export.json");
            json.Position = 0;
            await using (var target = entry.Open())
            {
                await json.CopyToAsync(target);
            }
        }
        zip.Position = 0;
        var fromZip = await import.AnalyzeAsync(zip);

        Assert.Equal(fromJson.Posts, fromZip.Posts);
        Assert.Equal(fromJson.Media, fromZip.Media);
    }

    [PostgresFact]
    public async Task Archive_export_endpoint_streams_zip_over_bff()
    {
        using var actor = await App.AdministratorAsync();
        await actor.Content.SaveAsync(NewContent("API 封裝", status: ContentStatus.Published));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        // BFF 合約：管理端點要求服務通行證＋有效的管理員工作階段戳記。
        client.DefaultRequestHeaders.Add(AdminSession.StampHeader, await App.AdminSecurityStampAsync());
        client.DefaultRequestHeaders.Add(AccessClaims.UserHeader, await App.AdminUserIdAsync());

        using var response = await client.GetAsync("/api/v1/admin/transfer/export-archive");
        Assert.True(response.IsSuccessStatusCode,
            $"status={(int)response.StatusCode} console={string.Join(" | ", api.ConsoleErrors)}");
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K');
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.NotNull(zip.GetEntry("origami-export.json"));
    }
}
