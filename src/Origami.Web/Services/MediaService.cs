namespace Origami.Web.Services;

public sealed class MediaService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin,
    StoragePaths storage, TimeProvider clock, ILogger<MediaService> logger)
{
    private static readonly SemaphoreSlim DecodeSlots = new(2);

    public async Task<PagedResult<MediaItem>> ListAsync(int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var offset = ContentRules.Offset(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var total = await db.MediaAssets.CountAsync(ct);
        var items = await db.MediaAssets.AsNoTracking().OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize).Select(x => new MediaItem(x.Id, x.FileName, x.OriginalName, x.ContentType, x.Size, x.Width, x.Height, x.UploadedAt)).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<MediaItem> UploadAsync(Stream stream, string originalName, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 200 || originalName.IndexOfAny(['/', '\\', ':']) >= 0
            || originalName.Any(char.IsControl) || originalName is "." or "..")
            throw new CmsException("圖片檔名不合法。");
        var originalExtension = Path.GetExtension(originalName).ToLowerInvariant();
        if (originalExtension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            throw new CmsException("僅接受 PNG、JPEG、WebP 點陣圖片，不接受 SVG。");

        await DecodeSlots.WaitAsync(ct);
        try
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
            using var data = SKData.CreateCopy(input.ToArray());
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
            var committed = false;
            try
            {
                await using (var file = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await file.WriteAsync(encoded.ToArray(), ct);
                    await file.FlushAsync(ct);
                }
                // The completed re-encoded image becomes public atomically; source bytes are never served.
                await admin.RequireAsync(ct);
                File.Move(stagingPath, finalPath);
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
                Cleanup(stagingPath);
                if (!committed) Cleanup(finalPath);
            }
        }
        finally { DecodeSlots.Release(); }
    }

    private void Cleanup(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { logger.LogError("無法清理本次上傳暫存檔，請檢查儲存目錄權限。"); }
        catch (UnauthorizedAccessException) { logger.LogError("無法清理本次上傳暫存檔，請檢查儲存目錄權限。"); }
    }
}
