namespace Origami.Core.Services;

public sealed partial class MediaService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin,
    StoragePaths storage, TimeProvider clock, ILogger<MediaService> logger)
{
    private static readonly SemaphoreSlim DecodeSlots = new(2);

    public async Task<PagedResult<MediaItem>> ListAsync(int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default, string? search = null, MediaKind? kind = null)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        var offset = ContentRules.Offset(page, pageSize);
        if (kind is { } filterKind && !Enum.IsDefined(filterKind)) throw new CmsException("媒體種類無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MediaAssets.AsNoTracking();
        if (kind is { } targetKind)
            query = query.Where(x => x.Kind == targetKind);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // 管理員以上傳名稱、替代文字或說明文字搜尋圖片。
            var pattern = "%" + ContentRules.EscapeLike(search.Trim()) + "%";
            query = query.Where(x => EF.Functions.ILike(x.OriginalName, pattern) || EF.Functions.ILike(x.AltText, pattern) || EF.Functions.ILike(x.Caption, pattern));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize).Select(x => new MediaItem(x.Id, x.FileName, x.OriginalName, x.ContentType, x.Size, x.Width, x.Height, x.UploadedAt)
            { AltText = x.AltText, Caption = x.Caption, Kind = x.Kind }).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<MediaItem> UpdateMetaAsync(Guid id, string? altText, string? caption, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var alt = (altText ?? "").Trim();
        if (alt.Length > Limits.AltText) throw new CmsException($"替代文字上限為 {Limits.AltText} 字。");
        var cap = (caption ?? "").Trim();
        if (cap.Length > Limits.Caption) throw new CmsException($"說明文字上限為 {Limits.Caption} 字。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var asset = await db.MediaAssets.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("圖片不存在，請重新載入媒體庫。");
        asset.AltText = alt;
        asset.Caption = cap;
        await ContentRules.SaveAsync(db, ct);
        return new(asset.Id, asset.FileName, asset.OriginalName, asset.ContentType, asset.Size, asset.Width, asset.Height, asset.UploadedAt)
        { AltText = asset.AltText, Caption = asset.Caption, Kind = asset.Kind };
    }

    /// <summary>回應式寬度的變體檔名，例如 abc.png → abc_w480.png。</summary>
    public static string VariantName(string fileName, int width) =>
        Path.GetFileNameWithoutExtension(fileName) + "_w" + width + Path.GetExtension(fileName);

    public sealed record MediaReference(Guid ContentId, string Title, ContentKind Kind, ContentStatus Status);

    /// <summary>Markdown 內嵌此圖片的內容，不限狀態——草稿或回收桶內容仍持有引用。</summary>
    public async Task<List<MediaReference>> ListReferencesAsync(Guid mediaId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var fileName = await db.MediaAssets.Where(x => x.Id == mediaId).Select(x => x.FileName).SingleOrDefaultAsync(ct)
            ?? throw new CmsException("圖片不存在，請重新載入媒體庫。");
        return await QueryReferencesAsync(db, mediaId, fileName, ct);
    }

    public async Task<int> CountReferencesAsync(Guid mediaId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var fileName = await db.MediaAssets.Where(x => x.Id == mediaId).Select(x => x.FileName).SingleOrDefaultAsync(ct)
            ?? throw new CmsException("圖片不存在，請重新載入媒體庫。");
        return await ReferencedContents(db, mediaId, fileName).CountAsync(ct);
    }

    /// <summary>
    /// 一次取回整頁媒體的引用數；供列表端點填入 <see cref="MediaItem.ReferenceCount"/>，避免呼叫端逐筆往返。
    /// 已知取舍：引用標記含響應式變體（每檔多名），無法以單條 GROUP BY 聚合，
    /// 因此逐媒體計數——每頁至多 <see cref="Limits.MaxPageSize"/> 條順序查詢，僅管理端列表使用。
    /// </summary>
    public async Task<Dictionary<Guid, int>> ReferenceCountsAsync(IReadOnlyCollection<Guid> mediaIds, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        if (mediaIds.Count == 0) return [];
        await using var db = await factory.CreateDbContextAsync(ct);
        var names = await db.MediaAssets.AsNoTracking().Where(x => mediaIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FileName, ct);
        var counts = new Dictionary<Guid, int>();
        foreach (var id in mediaIds)
            counts[id] = names.TryGetValue(id, out var fileName) ? await ReferencedContents(db, id, fileName).CountAsync(ct) : 0;
        return counts;
    }

    /// <summary>Markdown 本文可用來引用圖片的標記：原圖與所有變體。</summary>
    public static string[] ReferenceMarkers(string fileName)
    {
        var markers = new List<string> { "/media/" + fileName };
        foreach (var width in Limits.ImageVariantWidths)
            markers.Add("/media/" + VariantName(fileName, width));
        return [.. markers];
    }

    /// <summary>以檔名標記內嵌此圖片的內容查詢；不含封面引用。</summary>
    private static IQueryable<Content> ReferencedByMarkdown(CmsDbContext db, string fileName)
    {
        var markers = ReferenceMarkers(fileName);
        // 任一標記都算引用：本文可能內嵌原圖或變體。
        var parameter = Expression.Parameter(typeof(Content), "x");
        var contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        Expression? body = null;
        foreach (var marker in markers)
        {
            var markdown = Expression.Property(parameter, nameof(Content.Markdown));
            var call = Expression.Call(markdown, contains, Expression.Constant(marker));
            body = body is null ? call : Expression.OrElse(body, call);
        }
        var predicate = Expression.Lambda<Func<Content, bool>>(body!, parameter);
        return db.Contents.AsNoTracking().Where(predicate);
    }

    /// <summary>內嵌此圖片（含變體）或以它作為封面的內容，不限狀態。</summary>
    private static IQueryable<Content> ReferencedContents(CmsDbContext db, Guid mediaId, string fileName) =>
        ReferencedByMarkdown(db, fileName).Union(db.Contents.AsNoTracking().Where(x => x.CoverMediaId == mediaId));

    private static async Task<List<MediaReference>> QueryReferencesAsync(CmsDbContext db, Guid mediaId, string fileName, CancellationToken ct)
    {
        return await ReferencedContents(db, mediaId, fileName)
            .OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Select(x => new MediaReference(x.Id, x.Title, x.Kind, x.Status)).ToListAsync(ct);
    }

    /// <summary>設定的網站圖示背後的儲存檔與內容類型；未設定時為 null。</summary>
    public async Task<SiteIconFile?> GetSiteIconAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var icon = await (from settings in db.Settings.AsNoTracking()
                          join asset in db.MediaAssets.AsNoTracking() on settings.SiteIconMediaId equals asset.Id
                          where settings.Id == 1
                          select new { asset.FileName, asset.ContentType }).SingleOrDefaultAsync(ct);
        // 不是單純圖片檔的儲存檔名絕不會變成可讀取的路徑。
        if (icon is null || !ContentRules.IsSafeMediaFileName(icon.FileName)) return null;
        return new SiteIconFile(Path.Combine(storage.Images, icon.FileName), icon.ContentType);
    }

    public sealed record SiteIconFile(string Path, string ContentType);

    /// <summary>設定的網站標誌背後的儲存檔名；未設定時為 null（頁首僅顯示站名文字）。</summary>
    public async Task<string?> GetSiteLogoFileNameAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var logo = await (from settings in db.Settings.AsNoTracking()
                          join asset in db.MediaAssets.AsNoTracking() on settings.LogoMediaId equals asset.Id
                          where settings.Id == 1
                          select asset.FileName).SingleOrDefaultAsync(ct);
        return logo is not null && ContentRules.IsSafeMediaFileName(logo) ? logo : null;
    }

    /// <summary>是否為圖片副檔名；圖片走重新編碼管線，其餘走原樣儲存管線。</summary>
    public static bool IsImageFileName(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";

    /// <summary>
    /// 上傳非圖片媒體（PDF／音訊／影片）：以魔數判定實際格式，副檔名不符即拒絕；
    /// 原樣儲存不重新編碼，檔名由伺服器產生，不含使用者提供的路徑。
    /// </summary>
    public async Task<MediaItem> UploadFileAsync(Stream stream, string originalName, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var safeName = ValidateOriginalName(originalName);
        var head = new byte[4096];
        var read = await stream.ReadAsync(head, ct);
        var detected = DetectFileKind(head.AsSpan(0, read));
        if (detected is null)
            throw new CmsException("不支援的檔案格式；僅接受 PDF、MP3、M4A、MP4、WebM、OGG、WAV。");
        var (kind, extension, contentType) = detected.Value;
        if (Path.GetExtension(safeName).ToLowerInvariant() != extension)
            throw new CmsException("檔案副檔名與實際格式不符。");
        if (stream.CanSeek && stream.Length > Limits.FileUploadBytes)
            throw new CmsException($"檔案超過 {Limits.FileUploadBytes / 1024 / 1024} MB 上限。");

        var id = Guid.NewGuid();
        var fileName = id.ToString("N") + extension;
        var path = Path.Combine(storage.Files, fileName);
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await file.WriteAsync(head.AsMemory(0, read), ct);
                // 有界複製：超過上限就立刻停止，不讓整個串流先寫進磁碟再事後判退。
                var buffer = new byte[81920];
                var written = read;
                while (true)
                {
                    var count = await stream.ReadAsync(buffer, ct);
                    if (count == 0) break;
                    written += count;
                    if (written > Limits.FileUploadBytes)
                        throw new CmsException($"檔案超過 {Limits.FileUploadBytes / 1024 / 1024} MB 上限。");
                    await file.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                await file.FlushAsync(ct);
            }
            var size = new FileInfo(path).Length;
            if (size is 0 or > Limits.FileUploadBytes)
                throw new CmsException($"檔案大小須為 1 位元組至 {Limits.FileUploadBytes} 位元組。");
            await using var db = await factory.CreateDbContextAsync(ct);
            var asset = new MediaAsset
            {
                Id = id, SiteId = db.CurrentSiteId, FileName = fileName, OriginalName = safeName,
                ContentType = contentType, Kind = kind, Size = size,
                UploadedAt = clock.GetUtcNow()
            };
            db.MediaAssets.Add(asset);
            await ContentRules.SaveAsync(db, ct);
            return new(asset.Id, asset.FileName, asset.OriginalName, asset.ContentType, asset.Size, 0, 0, asset.UploadedAt)
            { Kind = kind };
        }
        catch
        {
            // 失敗時不得留下可公開讀取的半成品。
            Cleanup(path);
            throw;
        }
    }

    /// <summary>以魔數判定非圖片媒體的種類；不认识的內容一律視為不支援。</summary>
    private static (MediaKind Kind, string Extension, string ContentType)? DetectFileKind(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 5 && head[..5].SequenceEqual("%PDF-"u8))
            return (MediaKind.Document, ".pdf", "application/pdf");
        if (head.Length >= 3 && head[..3].SequenceEqual("ID3"u8))
            return (MediaKind.Audio, ".mp3", "audio/mpeg");
        if (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0)
            return (MediaKind.Audio, ".mp3", "audio/mpeg");
        if (head.Length >= 4 && head[..4].SequenceEqual("OggS"u8))
            return (MediaKind.Audio, ".ogg", "audio/ogg");
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WAVE"u8))
            return (MediaKind.Audio, ".wav", "audio/wav");
        if (head.Length >= 4 && head[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
            return (MediaKind.Video, ".webm", "video/webm");
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = head[8..12];
            if (brand.SequenceEqual("M4A "u8) || brand.SequenceEqual("M4B "u8))
                return (MediaKind.Audio, ".m4a", "audio/mp4");
            return (MediaKind.Video, ".mp4", "video/mp4");
        }
        return null;
    }

    /// <summary>只做檔名安全檢查（不含副檔名白名單）；上傳檔案的副檔名由魔數決定。</summary>
    private static string ValidateOriginalName(string originalName)
    {
        if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 200 || originalName.IndexOfAny(['/', '\\', ':']) >= 0
            || originalName.Any(char.IsControl) || originalName is "." or "..")
            throw new CmsException("檔名不合法。");
        return originalName;
    }

    /// <summary>連同變體刪除未被引用的圖片；仍被引用的圖片予以拒絕。</summary>
    public async Task DeleteAsync(Guid mediaId, bool confirmed, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (!confirmed) throw new CmsException("請先確認刪除圖片。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var asset = await db.MediaAssets.SingleOrDefaultAsync(x => x.Id == mediaId, ct) ?? throw new CmsException("圖片不存在，請重新載入媒體庫。");
        // 儲存檔名決定路徑，因此必須是媒體目錄內的單純檔案。
        if (!ContentRules.IsSafeMediaFileNameAny(asset.FileName))
            throw new CmsException("媒體檔名不合法，無法刪除。");
        if (await db.Settings.AsNoTracking().AnyAsync(s => s.Id == 1 && (s.SiteIconMediaId == mediaId || s.LogoMediaId == mediaId), ct))
            throw new CmsException("此圖片是目前的網站圖示或標誌，請先在網站設定改用其他圖片或清除後再刪除。");
        var references = await QueryReferencesAsync(db, mediaId, asset.FileName, ct);
        if (references.Count > 0)
        {
            var titles = string.Join("、", references.Take(3).Select(x => "「" + x.Title + "」"));
            var more = references.Count > 3 ? "等共 " + references.Count + " 篇內容" : "";
            throw new CmsException("圖片仍被 " + titles + more + " 引用，請先移除引用或更換圖片後再刪除。");
        }
        db.MediaAssets.Remove(asset);
        await ContentRules.SaveAsync(db, ct);
        // 資料列已刪除；檔案移除採盡力而為，絕不阻擋資料庫結果。
        if (asset.Kind == MediaKind.Image)
        {
            Cleanup(Path.Combine(storage.Images, asset.FileName));
            foreach (var width in Limits.ImageVariantWidths)
                Cleanup(Path.Combine(storage.Images, VariantName(asset.FileName, width)));
        }
        else
        {
            Cleanup(Path.Combine(storage.Files, asset.FileName));
        }
    }

    /// <summary>系統操作：為變體功能出現前上傳的圖片重新產生缺少的變體檔。</summary>
    public async Task<int> BackfillVariantsAsync(CancellationToken ct = default)
    {
        var repaired = 0;
        await using var db = await factory.CreateDbContextAsync(ct);
        var assets = await db.MediaAssets.AsNoTracking()
            .Select(x => new { x.FileName, x.Width, x.Height, x.ContentType }).ToListAsync(ct);
        foreach (var asset in assets)
        {
            if (!ContentRules.IsSafeMediaFileName(asset.FileName)) continue;
            var originalPath = Path.Combine(storage.Images, asset.FileName);
            if (!File.Exists(originalPath)) continue;
            var format = asset.ContentType switch
            {
                "image/png" => SKEncodedImageFormat.Png,
                "image/jpeg" => SKEncodedImageFormat.Jpeg,
                "image/webp" => SKEncodedImageFormat.Webp,
                _ => (SKEncodedImageFormat?)null
            };
            if (format is null) continue;
            foreach (var width in Limits.ImageVariantWidths)
            {
                ct.ThrowIfCancellationRequested();
                if (width >= asset.Width) break;
                var variantPath = Path.Combine(storage.Images, VariantName(asset.FileName, width));
                if (File.Exists(variantPath)) continue;
                if (TryReadVariant(originalPath, format.Value, width, out var bytes))
                {
                    await File.WriteAllBytesAsync(variantPath, bytes, ct);
                    repaired++;
                }
            }
        }
        return repaired;
    }

    private static bool TryReadVariant(string originalPath, SKEncodedImageFormat format, int width, out byte[] bytes)
    {
        bytes = [];
        try
        {
            using var data = SKData.Create(originalPath);
            using var codec = SKCodec.Create(data);
            if (codec is null) return false;
            var info = codec.Info;
            var height = Math.Max(1, (int)Math.Round((double)info.Height * width / info.Width));
            using var source = new SKBitmap();
            if (!source.TryAllocPixels(info) || codec.GetPixels(info, source.GetPixels()) != SKCodecResult.Success) return false;
            using var target = new SKBitmap(new SKImageInfo(width, height));
            if (!source.ScalePixels(target, new SKSamplingOptions(SKCubicResampler.Mitchell))) return false;
            using var image = SKImage.FromBitmap(target);
            using var encoded = image.Encode(format, 85);
            if (encoded is null) return false;
            bytes = encoded.ToArray();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>公開圖片檔名與像素寬度的匿名目錄，用於組出 srcset 屬性。</summary>
    public async Task<Dictionary<string, int>> PublicImageWidthsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MediaAssets.AsNoTracking().ToDictionaryAsync(x => x.FileName, x => x.Width, ct);
    }

    public async Task<MediaItem> UploadAsync(Stream stream, string originalName, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var originalExtension = ValidateFileName(originalName);

        await DecodeSlots.WaitAsync(ct);
        try
        {
            using var data = SKData.CreateCopy(await ReadUploadAsync(stream, ct));
            using var codec = SKCodec.Create(data);
            if (codec is null) throw new CmsException("圖片無法解碼或格式不合法。");
            var (extension, contentType, format) = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Png => (".png", "image/png", SKEncodedImageFormat.Png),
                SKEncodedImageFormat.Jpeg => (".jpg", "image/jpeg", SKEncodedImageFormat.Jpeg),
                SKEncodedImageFormat.Webp => (".webp", "image/webp", SKEncodedImageFormat.Webp),
                _ => throw new CmsException("實際圖片格式不在允許清單中。")
            };
            if (originalExtension != extension && !(extension == ".jpg" && originalExtension == ".jpeg"))
                throw new CmsException("圖片副檔名與實際格式不符。");
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || info.Width > Limits.ImageDimension || info.Height > Limits.ImageDimension
                || (long)info.Width * info.Height > Limits.ImagePixels || codec.FrameCount > 1)
                throw new CmsException("圖片尺寸過大或含動畫；上限 2,000 萬像素、單邊 10,000 像素。");
            using var bitmap = new SKBitmap();
            if (!bitmap.TryAllocPixels(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul))
                || codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new CmsException("圖片資料不完整或無法解碼。");
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(format, 90) ?? throw new CmsException("圖片重新編碼失敗。");
            if (encoded.Size > Limits.UploadBytes) throw new CmsException("圖片重新編碼後超過 5 MiB，請縮小圖片。");
            ct.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            var fileName = id.ToString("N") + extension;
            var stagingPath = Path.Combine(storage.Staging, id.ToString("N") + ".tmp");
            var finalPath = Path.Combine(storage.Images, fileName);
            // 回應式變體：僅產生比原始圖窄的寬度，絕不放大。
            var variants = new List<(string Name, byte[] Bytes)>();
            foreach (var width in Limits.ImageVariantWidths)
            {
                if (width >= info.Width) break;
                var height = Math.Max(1, (int)Math.Round((double)info.Height * width / info.Width));
                using var target = new SKBitmap(new SKImageInfo(width, height));
                if (!bitmap.ScalePixels(target, new SKSamplingOptions(SKCubicResampler.Mitchell))) continue;
                using var scaled = SKImage.FromBitmap(target);
                using var encodedVariant = scaled.Encode(format, 85);
                if (encodedVariant is null) continue;
                variants.Add((VariantName(fileName, width), encodedVariant.ToArray()));
            }
            var committed = false;
            var staged = new List<(string Staging, string Final)> { (stagingPath, finalPath) };
            try
            {
                foreach (var (name, _) in variants)
                    staged.Add((Path.Combine(storage.Staging, Path.GetFileNameWithoutExtension(name) + ".tmp"),
                        Path.Combine(storage.Images, name)));
                for (var i = 0; i < staged.Count; i++)
                {
                    var (staging, _) = staged[i];
                    await using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                    {
                        await file.WriteAsync(i == 0 ? encoded.ToArray() : variants[i - 1].Bytes, ct);
                        await file.FlushAsync(ct);
                    }
                }
                // 完成重新編碼的圖片以原子方式公開；原始位元組絕不對外提供。
                await admin.RequireAsync(ct);
                foreach (var (staging, final) in staged) File.Move(staging, final);
                await using var db = await factory.CreateDbContextAsync(ct);
                var asset = new MediaAsset
                {
                    Id = id, SiteId = db.CurrentSiteId, FileName = fileName, OriginalName = originalName, ContentType = contentType,
                    Size = encoded.Size, Width = info.Width, Height = info.Height, UploadedAt = clock.GetUtcNow()
                };
                db.MediaAssets.Add(asset);
                await ContentRules.SaveAsync(db, ct);
                committed = true;
                return new(asset.Id, asset.FileName, asset.OriginalName, asset.ContentType, asset.Size, asset.Width, asset.Height, asset.UploadedAt);
            }
            finally
            {
                foreach (var (staging, final) in staged)
                {
                    Cleanup(staging);
                    if (!committed) Cleanup(final);
                }
            }
        }
        finally { DecodeSlots.Release(); }
    }

    private static string ValidateFileName(string originalName)
    {
        if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 200 || originalName.IndexOfAny(['/', '\\', ':']) >= 0
            || originalName.Any(char.IsControl) || originalName is "." or "..")
            throw new CmsException("圖片檔名不合法。");
        var originalExtension = Path.GetExtension(originalName).ToLowerInvariant();
        if (originalExtension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            throw new CmsException("僅接受 PNG、JPEG、WebP 點陣圖片，不接受 SVG。");
        return originalExtension;
    }

    private static async Task<byte[]> ReadUploadAsync(Stream stream, CancellationToken ct)
    {
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (input.Length + count > Limits.UploadBytes) throw new CmsException("圖片不可超過 5 MiB。");
            await input.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        if (input.Length == 0) throw new CmsException("圖片不可為空檔案。");
        return input.ToArray();
    }

    private void Cleanup(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.CleanupFailed(logger, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Error, "無法清理本次上傳暫存檔，請檢查儲存目錄權限。")]
        public static partial void CleanupFailed(ILogger logger, Exception exception);
    }
}
