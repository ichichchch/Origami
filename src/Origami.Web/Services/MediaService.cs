namespace Origami.Web.Services;

public sealed partial class MediaService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin,
    StoragePaths storage, TimeProvider clock, ILogger<MediaService> logger)
{
    private static readonly SemaphoreSlim DecodeSlots = new(2);

    public async Task<PagedResult<MediaItem>> ListAsync(int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default, string? search = null)
    {
        await admin.RequireAsync(ct);
        var offset = ContentRules.Offset(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MediaAssets.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Administrators find images by the name they uploaded or the alt text they wrote.
            var pattern = "%" + ContentRules.EscapeLike(search.Trim()) + "%";
            query = query.Where(x => EF.Functions.ILike(x.OriginalName, pattern) || EF.Functions.ILike(x.AltText, pattern));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize).Select(x => new MediaItem(x.Id, x.FileName, x.OriginalName, x.ContentType, x.Size, x.Width, x.Height, x.UploadedAt)
            { AltText = x.AltText }).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<MediaItem> UpdateAltAsync(Guid id, string? altText, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var alt = (altText ?? "").Trim();
        if (alt.Length > Limits.AltText) throw new CmsException($"替代文字上限為 {Limits.AltText} 字。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var asset = await db.MediaAssets.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("圖片不存在，請重新載入媒體庫。");
        asset.AltText = alt;
        await ContentRules.SaveAsync(db, ct);
        return new(asset.Id, asset.FileName, asset.OriginalName, asset.ContentType, asset.Size, asset.Width, asset.Height, asset.UploadedAt) { AltText = asset.AltText };
    }

    /// <summary>Variant file name for a responsive width, e.g. abc.png → abc_w480.png.</summary>
    public static string VariantName(string fileName, int width) =>
        Path.GetFileNameWithoutExtension(fileName) + "_w" + width + Path.GetExtension(fileName);

    public sealed record MediaReference(Guid ContentId, string Title, ContentKind Kind, ContentStatus Status);

    /// <summary>Contents whose Markdown embeds this image, in any state — a draft or trashed piece still owns the reference.</summary>
    public async Task<List<MediaReference>> ListReferencesAsync(Guid mediaId, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var fileName = await db.MediaAssets.Where(x => x.Id == mediaId).Select(x => x.FileName).SingleOrDefaultAsync(ct)
            ?? throw new CmsException("圖片不存在，請重新載入媒體庫。");
        return await QueryReferencesAsync(db, fileName, ct);
    }

    public async Task<int> CountReferencesAsync(Guid mediaId, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var fileName = await db.MediaAssets.Where(x => x.Id == mediaId).Select(x => x.FileName).SingleOrDefaultAsync(ct)
            ?? throw new CmsException("圖片不存在，請重新載入媒體庫。");
        return await db.Contents.AsNoTracking().CountAsync(x => x.Markdown.Contains("/media/" + fileName), ct);
    }

    private static async Task<List<MediaReference>> QueryReferencesAsync(CmsDbContext db, string fileName, CancellationToken ct)
    {
        var marker = "/media/" + fileName;
        return await db.Contents.AsNoTracking().Where(x => x.Markdown.Contains(marker))
            .OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Select(x => new MediaReference(x.Id, x.Title, x.Kind, x.Status)).ToListAsync(ct);
    }

    /// <summary>Deletes an unreferenced image together with its variants. Referenced images are refused.</summary>
    public async Task DeleteAsync(Guid mediaId, bool confirmed, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (!confirmed) throw new CmsException("請先確認刪除圖片。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var asset = await db.MediaAssets.SingleOrDefaultAsync(x => x.Id == mediaId, ct) ?? throw new CmsException("圖片不存在，請重新載入媒體庫。");
        var references = await QueryReferencesAsync(db, asset.FileName, ct);
        if (references.Count > 0)
        {
            var titles = string.Join("、", references.Take(3).Select(x => "「" + x.Title + "」"));
            var more = references.Count > 3 ? "等共 " + references.Count + " 篇內容" : "";
            throw new CmsException("圖片仍被 " + titles + more + " 引用，請先移除引用或更換圖片後再刪除。");
        }
        db.MediaAssets.Remove(asset);
        await ContentRules.SaveAsync(db, ct);
        // The row is gone; file removal is best-effort and never blocks the database outcome.
        Cleanup(Path.Combine(storage.Images, asset.FileName));
        foreach (var width in Limits.ImageVariantWidths)
            Cleanup(Path.Combine(storage.Images, VariantName(asset.FileName, width)));
    }

    /// <summary>System operation: regenerates missing variant files for assets uploaded before variants existed.</summary>
    public async Task<int> BackfillVariantsAsync(CancellationToken ct = default)
    {
        var repaired = 0;
        await using var db = await factory.CreateDbContextAsync(ct);
        var assets = await db.MediaAssets.AsNoTracking()
            .Select(x => new { x.FileName, x.Width, x.Height, x.ContentType }).ToListAsync(ct);
        foreach (var asset in assets)
        {
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

    /// <summary>Anonymous catalog of public image names and their pixel widths, used to build srcset attributes.</summary>
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
            // Responsive variants: only widths narrower than the source, never upscaled.
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
                // The completed re-encoded images become public atomically; source bytes are never served.
                await admin.RequireAsync(ct);
                foreach (var (staging, final) in staged) File.Move(staging, final);
                await using var db = await factory.CreateDbContextAsync(ct);
                var asset = new MediaAsset
                {
                    Id = id, FileName = fileName, OriginalName = originalName, ContentType = contentType,
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
