namespace Origami.Tests;

/// <summary>非圖片媒體上傳：以魔數驗證 PDF／音訊／影片，副檔名不符或未知格式一律拒絕。</summary>
[Collection(PostgresCollection.Name)]
public sealed class MediaFileUploadTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static MediaService CreateMediaService(TestActor actor, TemporaryMediaStorage directory) => new(
        actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
        actor.Services.GetRequiredService<AdminAccess>(), directory.Create(), TimeProvider.System, NullLogger<MediaService>.Instance);

    private static byte[] Pdf() => "%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n"u8.ToArray();

    private static byte[] Mp3() => [.. "ID3"u8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A];

    private static byte[] Mp4() => [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0x00, 0x00, 0x00, 0x00];

    private static byte[] M4a() => [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ', 0x00, 0x00, 0x00, 0x00];

    private static byte[] Webm() => [0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x02, 0x03, 0x04];

    private static byte[] Xlsx() => ZipPackage("[Content_Types].xml", "xl/workbook.xml");

    /// <summary>以兩個未壓縮項目組出最小 OOXML 容器；項目名以明文出現在本地檔頭，供偵測邏輯判讀。</summary>
    private static byte[] ZipPackage(string first, string second)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in new[] { (first, "<?xml version=\"1.0\"?><Types/>"), (second, "<workbook/>") })
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return buffer.ToArray();
    }

    private static byte[] Csv() => [0xEF, 0xBB, 0xBF, .. "員工,日期\nAubrey,2026-10-28\n"u8];

    [PostgresFact]
    public async Task Allowed_file_types_are_stored_with_their_kind_and_original_bytes()
    {
        using var admin = await App.AdministratorAsync();
        using var directory = new TemporaryMediaStorage();
        var media = CreateMediaService(admin, directory);

        using (var stream = new MemoryStream(Pdf()))
        {
            var item = await media.UploadFileAsync(stream, "manual.pdf");
            Assert.Equal(MediaKind.Document, item.Kind);
            Assert.Equal("application/pdf", item.ContentType);
            Assert.Equal(".pdf", Path.GetExtension(item.FileName));
            // 非圖片的 Markdown 是連結，不是圖片語法。
            Assert.StartsWith("[", item.Markdown);
            Assert.DoesNotContain("![", item.Markdown);
        }
        using (var stream = new MemoryStream(Mp3()))
        {
            var item = await media.UploadFileAsync(stream, "song.mp3");
            Assert.Equal(MediaKind.Audio, item.Kind);
            Assert.Equal("audio/mpeg", item.ContentType);
        }
        using (var stream = new MemoryStream(Mp4()))
        {
            var item = await media.UploadFileAsync(stream, "clip.mp4");
            Assert.Equal(MediaKind.Video, item.Kind);
            Assert.Equal("video/mp4", item.ContentType);
        }
        using (var stream = new MemoryStream(M4a()))
        {
            var item = await media.UploadFileAsync(stream, "voice.m4a");
            Assert.Equal(MediaKind.Audio, item.Kind);
            Assert.Equal("audio/mp4", item.ContentType);
        }
        using (var stream = new MemoryStream(Webm()))
        {
            var item = await media.UploadFileAsync(stream, "clip.webm");
            Assert.Equal(MediaKind.Video, item.Kind);
        }

        // 儲存檔案確實存在於媒體目錄，且內容未被改寫。
        await using var db = await App.CreateContextAsync();
        var asset = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.OriginalName == "manual.pdf");
        var path = Path.Combine(directory.Create().Files, asset.FileName);
        Assert.True(File.Exists(path));
        Assert.Equal("%PDF-1.7", File.ReadAllText(path)[..8]);
    }

    [PostgresFact]
    public async Task Spreadsheet_uploads_are_stored_as_documents()
    {
        using var admin = await App.AdministratorAsync();
        using var directory = new TemporaryMediaStorage();
        var media = CreateMediaService(admin, directory);

        using (var stream = new MemoryStream(Xlsx()))
        {
            var item = await media.UploadFileAsync(stream, "sheet.xlsx");
            Assert.Equal(MediaKind.Document, item.Kind);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", item.ContentType);
            Assert.Equal(".xlsx", Path.GetExtension(item.FileName));
            // 試算表與其他非圖片一樣以連結插入，不是圖片語法。
            Assert.StartsWith("[", item.Markdown);
            Assert.DoesNotContain("![", item.Markdown);
        }
        using (var stream = new MemoryStream(Csv()))
        {
            var item = await media.UploadFileAsync(stream, "attendance.csv");
            Assert.Equal(MediaKind.Document, item.Kind);
            Assert.Equal("text/csv", item.ContentType);
            Assert.Equal(".csv", Path.GetExtension(item.FileName));
        }

        await using var db = await App.CreateContextAsync();
        var xlsx = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.OriginalName == "sheet.xlsx");
        var path = Path.Combine(directory.Create().Files, xlsx.FileName);
        Assert.True(File.Exists(path));
        // 原樣儲存：首位元組仍為 ZIP 容器的 'P'。
        Assert.Equal(0x50, File.ReadAllBytes(path)[0]);
    }

    [PostgresFact]
    public async Task Spreadsheet_disguises_are_refused()
    {
        using var admin = await App.AdministratorAsync();
        using var directory = new TemporaryMediaStorage();
        var media = CreateMediaService(admin, directory);

        // OOXML 但不是試算表（docx 的零件）。
        using (var stream = new MemoryStream(ZipPackage("[Content_Types].xml", "word/document.xml")))
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "doc.xlsx"));
        // 一般 ZIP 沒有 OOXML 試算表特徵。
        using (var stream = new MemoryStream(ZipPackage("hello.txt", "world.txt")))
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "plain.xlsx"));
        // 執行檔偽裝成 xlsx。
        using (var stream = new MemoryStream([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, .. new byte[100]]))
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "evil.xlsx"));
        // 巨集活頁簿副檔名不在白名單。
        using (var stream = new MemoryStream(Xlsx()))
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "macro.xlsm"));
        // 副檔名與實際內容不符。
        using (var stream = new MemoryStream(Csv()))
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "table.xlsx"));
        // CSV 限 UTF-8：Big5 內容通過前段掃描後仍被全檔複驗拒絕。
        using (var stream = new MemoryStream([0xA4, 0xA4, 0xA4, 0xE5, 0x0A]))
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "big5.csv"));
        // 含 NUL 控制字元的二進位偽裝。
        using (var stream = new MemoryStream([0x31, 0x2C, 0x00, 0x32, 0x0A]))
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "binary.csv"));

        // 失敗不得留下任何資料列。
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.MediaAssets.AsNoTracking().ToListAsync());
    }

    [PostgresFact]
    public async Task Mismatched_extensions_and_unknown_formats_are_refused()
    {
        using var admin = await App.AdministratorAsync();
        using var directory = new TemporaryMediaStorage();
        var media = CreateMediaService(admin, directory);

        // 偽裝副檔名：PDF 內容卻命名為 mp3。
        using (var stream = new MemoryStream(Pdf()))
        {
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "fake.mp3"));
        }
        // 未知格式。
        using (var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }))
        {
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "mystery.txt"));
        }
        // 危險檔名不得進入儲存。
        using (var stream = new MemoryStream(Pdf()))
        {
            await Assert.ThrowsAsync<CmsException>(() => media.UploadFileAsync(stream, "../escape.pdf"));
        }

        // 失敗不得留下任何資料列或檔案。
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.MediaAssets.AsNoTracking().ToListAsync());
    }

    [PostgresFact]
    public async Task Non_image_media_can_be_deleted_and_its_file_removed()
    {
        using var admin = await App.AdministratorAsync();
        using var directory = new TemporaryMediaStorage();
        var media = CreateMediaService(admin, directory);
        using var stream = new MemoryStream(Pdf());
        var item = await media.UploadFileAsync(stream, "removable.pdf");
        var path = Path.Combine(directory.Create().Files, item.FileName);
        Assert.True(File.Exists(path));

        await media.DeleteAsync(item.Id, confirmed: true);
        Assert.False(File.Exists(path));
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.MediaAssets.AsNoTracking().Where(x => x.Id == item.Id).ToListAsync());
    }
}
