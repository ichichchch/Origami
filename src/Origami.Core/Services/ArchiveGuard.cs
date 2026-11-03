namespace Origami.Core.Services;

/// <summary>封裝串流的共用防護：有界緩衝與逐區塊有界複製。
/// ZIP 條目標頭宣稱的大小不可信，解壓量一律以實際讀取計量。</summary>
internal static class ArchiveGuard
{
    /// <summary>把輸入完整緩衝進記憶體並以大小上限把關；空檔與超限同樣拒絕。</summary>
    public static async Task<MemoryStream> BufferAsync(Stream input, long maxBytes, string sizeError, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        if (buffer.Length == 0 || buffer.Length > maxBytes)
            throw new CmsException(sizeError);
        return buffer;
    }

    /// <summary>逐區塊複製並累計解壓後位元組數，超過上限立即中止，不把超限內容整包讀進記憶體。</summary>
    public static async Task<long> CopyBoundedAsync(Stream source, Stream target, long totalBytes, long maxBytes,
        string overflowError, CancellationToken ct)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) return totalBytes;
            totalBytes += read;
            if (totalBytes > maxBytes)
                throw new CmsException(overflowError);
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
