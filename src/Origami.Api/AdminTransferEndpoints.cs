namespace Origami.Api;

/// <summary>JSON 站點匯出／匯入；緩衝後一次輸出，失敗的傳輸不會以 200 串流出殘缺檔案。</summary>
public static class AdminTransferEndpoints
{
    public static void MapAdminTransferApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/transfer").RequireAuthorization();

        group.MapGet("/export", async (ExportService export, HttpContext http, CancellationToken ct) =>
        {
            // 篩選匯出：kind=Post/Page，category 僅與 Post 合用；驗證由服務層統一把關。
            ContentKind? kind = null;
            var rawKind = http.Request.Query["kind"].ToString();
            if (rawKind.Length > 0)
            {
                if (!Enum.TryParse<ContentKind>(rawKind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    return Results.BadRequest(new { message = "參數 kind 無效（Post 或 Page）。" });
                kind = parsed;
            }
            var category = http.Request.Query["category"].ToString();
            using var buffer = new MemoryStream();
            await export.WriteAsync(buffer, kind, category.Length == 0 ? null : category, ct);
            return Results.File(buffer.ToArray(), "application/json", "origami-export.json");
        });

        // 完整封裝：JSON 資料＋媒體檔案（原圖與變體）；緩衝完成後才回應，串流中斷不會產生殘缺檔。
        group.MapGet("/export-archive", async (ExportService export, CancellationToken ct) =>
        {
            var buffer = new MemoryStream();
            await export.WriteArchiveAsync(buffer, ct);
            // 寫入後 Position 停在結尾；串流回應從頭開始。
            buffer.Position = 0;
            return Results.File(buffer, "application/zip", "origami-export.zip");
        });

        // 匯入分析只預覽不落地；由呼叫端（網站 UI）先呈現預覽再決定套用。
        group.MapPost("/import/analyze", async (IFormFile file, ImportService import, CancellationToken ct) =>
            Results.Ok(await AnalyzeAsync(file, import, ct))).DisableAntiforgery();

        group.MapPost("/import/apply", async (IFormFile file, ImportService import, CancellationToken ct) =>
        {
            if (file.Length is 0 or > Limits.ArchiveMaxBytes)
                throw new CmsException($"匯入檔大小須為 1 位元組至 {Limits.ArchiveMaxBytes} 位元組。");
            using var stream = file.OpenReadStream();
            return Results.Ok(await import.ApplyAsync(stream, confirmed: true, ct));
        }).DisableAntiforgery();

        // WordPress WXR 匯出與匯入：先預覽再套用；套用是新增內容，不取代現有資料。支援選用附件下載遷移。
        group.MapGet("/export-wxr", async (WxrImportService wxr, CancellationToken ct) =>
        {
            using var buffer = new MemoryStream();
            await wxr.ExportAsync(buffer, ct);
            return Results.File(buffer.ToArray(), "application/xml", "origami-wxr-export.xml");
        });

        group.MapPost("/import-wxr/analyze", async (IFormFile file, WxrImportService wxr, CancellationToken ct) =>
        {
            if (file.Length is 0 or > WxrImportService.MaxBytes)
                throw new CmsException($"WXR 檔案大小須為 1 位元組至 {WxrImportService.MaxBytes} 位元組。");
            using var stream = file.OpenReadStream();
            return Results.Ok(await wxr.AnalyzeAsync(stream, ct));
        }).DisableAntiforgery();

        group.MapPost("/import-wxr/apply", async (IFormFile file, HttpContext http, WxrImportService wxr, CancellationToken ct) =>
        {
            if (file.Length is 0 or > WxrImportService.MaxBytes)
                throw new CmsException($"WXR 檔案大小須為 1 位元組至 {WxrImportService.MaxBytes} 位元組。");
            var downloadAttachments = http.Request.Query["downloadAttachments"].ToString() is "true" or "1";
            var allowedHosts = http.Request.Query["allowedHosts"].ToString();
            using var stream = file.OpenReadStream();
            return Results.Ok(await wxr.ImportAsync(stream, ct, new WxrImportService.WxrAttachmentOptions(downloadAttachments, allowedHosts)));
        }).DisableAntiforgery();
    }

    private static async Task<ImportService.ImportPreview> AnalyzeAsync(IFormFile file, ImportService import, CancellationToken ct)
    {
        if (file.Length is 0 or > Limits.ArchiveMaxBytes)
            throw new CmsException($"匯入檔大小須為 1 位元組至 {Limits.ArchiveMaxBytes} 位元組。");
        using var stream = file.OpenReadStream();
        return await import.AnalyzeAsync(stream, ct);
    }
}
