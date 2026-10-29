namespace Origami.Tests;

/// <summary>網站標誌：設定保存、檔名解析、使用中圖片刪除保護、匯出匯入往返。</summary>
[Collection(PostgresCollection.Name)]
public sealed class SiteLogoTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private async Task<MediaAsset> CreateAssetAsync(string originalName = "logo.png")
    {
        await using var db = await App.CreateContextAsync();
        var asset = new MediaAsset
        {
            FileName = Guid.NewGuid().ToString("N") + ".png", OriginalName = originalName,
            ContentType = "image/png", AltText = "標誌", Caption = "",
            Width = 320, Height = 96, UploadedAt = DateTimeOffset.UtcNow
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    [PostgresFact]
    public async Task Logo_persists_resolves_to_file_name_and_blocks_deletion_while_in_use()
    {
        using var admin = await App.AdministratorAsync();
        var asset = await CreateAssetAsync();

        var settings = await admin.Settings.GetAsync();
        settings.LogoMediaId = asset.Id;
        await admin.Settings.SaveAsync(settings);
        var saved = await admin.Settings.GetAsync();
        Assert.Equal(asset.Id, saved.LogoMediaId);

        var media = admin.Services.GetRequiredService<MediaService>();
        Assert.Equal(asset.FileName, await media.GetSiteLogoFileNameAsync());

        // 使用中的標誌不可刪除；訊息引導先清除設定。
        using var directory = new TemporaryMediaStorage();
        var deleter = new MediaService(
            admin.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            admin.Services.GetRequiredService<AdminAccess>(), directory.Create(),
            TimeProvider.System, NullLogger<MediaService>.Instance);
        var refused = await Assert.ThrowsAsync<CmsException>(() => deleter.DeleteAsync(asset.Id, confirmed: true));
        Assert.Contains("網站圖示或標誌", refused.Message, StringComparison.Ordinal);

        // 清除設定後即可再度刪除（檔案清理為盡力而為，資料列刪除為準）。
        saved.LogoMediaId = null;
        await admin.Settings.SaveAsync(saved);
        await deleter.DeleteAsync(asset.Id, confirmed: true);
        Assert.Null(await media.GetSiteLogoFileNameAsync());
    }

    [PostgresFact]
    public async Task Logo_survives_export_import_round_trip()
    {
        using var admin = await App.AdministratorAsync();
        var asset = await CreateAssetAsync();
        var settings = await admin.Settings.GetAsync();
        settings.LogoMediaId = asset.Id;
        await admin.Settings.SaveAsync(settings);

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var document = JsonNode.Parse(buffer.ToArray())!;
        Assert.Equal(asset.FileName, (string?)document["site"]!["logoFileName"]);

        using var input = new MemoryStream(buffer.ToArray());
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        await using var db = await App.CreateContextAsync();
        var restored = await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1);
        Assert.NotNull(restored.LogoMediaId);
        var restoredFile = await db.MediaAssets.AsNoTracking().Where(x => x.Id == restored.LogoMediaId)
            .Select(x => x.FileName).SingleAsync();
        Assert.Equal(asset.FileName, restoredFile);
    }
}
