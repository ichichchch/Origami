namespace Origami.Tests;

/// <summary>每日自動備份的核心：完整封裝落檔、保留份數上限與暫存清理。</summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Backup_creates_valid_archive_and_trims_to_keep()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("備份測試文章"));
        var backup = new BackupService(
            admin.Services.GetRequiredService<ExportService>(), App.Storage, TimeProvider.System,
            NullLogger<BackupService>.Instance);
        var options = new BackupOptions { Enabled = true, Keep = 1 };
        Directory.CreateDirectory(backup.BackupsDirectory);
        File.WriteAllBytes(Path.Combine(backup.BackupsDirectory, "origami-backup-20260101-000000.zip"), [1]);
        File.WriteAllBytes(Path.Combine(backup.BackupsDirectory, "origami-backup-20260102-000000.zip"), [1]);

        var result = await backup.RunAsync(options);

        Assert.True(File.Exists(result.Path));
        using (var zip = new ZipArchive(File.OpenRead(result.Path)))
        {
            Assert.NotNull(zip.GetEntry("origami-export.json"));
        }
        // 保留上限 1：兩個歷史備份被清掉，只剩本次。
        var remaining = Directory.GetFiles(backup.BackupsDirectory, "origami-backup-*.zip");
        Assert.Single(remaining);
        Assert.Equal(Path.GetFileName(result.Path), Path.GetFileName(remaining[0]));
        Assert.Equal(1, result.Retained);
        Assert.True(result.TotalBytes > 0);
        // 暫存檔不殘留；成功路徑只有最終 zip。
        Assert.Empty(Directory.GetFiles(backup.BackupsDirectory, "*.part"));
    }

    [PostgresFact]
    public async Task Backup_archives_non_image_media_alongside_images()
    {
        using var admin = await App.AdministratorAsync();
        var media = new MediaService(
            admin.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            admin.Services.GetRequiredService<AdminAccess>(), admin.Storage, TimeProvider.System,
            NullLogger<MediaService>.Instance);
        using (var stream = new MemoryStream("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n"u8.ToArray()))
            await media.UploadFileAsync(stream, "manual.pdf");

        var backup = new BackupService(
            admin.Services.GetRequiredService<ExportService>(), admin.Storage, TimeProvider.System,
            NullLogger<BackupService>.Instance);
        var result = await backup.RunAsync(new BackupOptions { Enabled = true, Keep = 1 });

        using var zip = new ZipArchive(File.OpenRead(result.Path));
        var entry = Assert.Single(zip.Entries, x => x.FullName.StartsWith("files/", StringComparison.Ordinal));
        Assert.EndsWith(".pdf", entry.FullName, StringComparison.Ordinal);
        using var reader = new StreamReader(entry.Open());
        // 備份內是原始位元組，解壓回 files/ 後即可由 /media 端點提供。
        Assert.StartsWith("%PDF-1.7", await reader.ReadToEndAsync(), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Backup_writes_only_the_current_sites_data_into_its_own_directory()
    {
        var site = await CreateSiteAsync("third");
        using var admin = await App.AdministratorAsync(site.Id);
        await admin.Content.SaveAsync(NewContent("第三站專屬文章"));
        var backup = new BackupService(
            admin.Services.GetRequiredService<ExportService>(), admin.Storage, TimeProvider.System,
            NullLogger<BackupService>.Instance);

        var result = await backup.RunAsync(new BackupOptions { Enabled = true, Keep = 2 });

        // 備份落在當站的儲存目錄，而不是預設站。
        Assert.StartsWith(Path.GetFullPath(admin.Storage.SiteRoot), Path.GetFullPath(result.Path));
        using (var zip = new ZipArchive(File.OpenRead(result.Path)))
        {
            var entry = zip.GetEntry("origami-export.json");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry.Open());
            // 匯出 JSON 對非 ASCII 採 \u 轉義，以剖析後的標題比對，不做原始子字串比對。
            var exported = JsonNode.Parse(await reader.ReadToEndAsync())!;
            Assert.Contains(exported["contents"]!.AsArray(),
                item => item!["title"]!.GetValue<string>() == "第三站專屬文章");
        }
        var defaultBackups = Path.Combine(App.Storage.SiteRoot, "backups");
        if (Directory.Exists(defaultBackups))
            Assert.DoesNotContain(Path.GetFileName(result.Path),
                Directory.GetFiles(defaultBackups).Select(Path.GetFileName));
    }
}
