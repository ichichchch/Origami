namespace Origami.Api;

/// <summary>JSON 站點匯出／匯入；緩衝後一次輸出，失敗的傳輸不會以 200 串流出殘缺檔案。</summary>
public static class AdminTransferEndpoints
{
    public static void MapAdminTransferApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/transfer").RequireAuthorization();

        group.MapGet("/export", async (ExportService export, CancellationToken ct) =>
        {
            using var buffer = new MemoryStream();
            await export.WriteAsync(buffer, ct);
            return Results.File(buffer.ToArray(), "application/json", "origami-export.json");
        });

        // 匯入分析只預覽不落地；由呼叫端（網站 UI）先呈現預覽再決定套用。
        group.MapPost("/import/analyze", async (IFormFile file, ImportService import, CancellationToken ct) =>
            Results.Ok(await AnalyzeAsync(file, import, ct))).DisableAntiforgery();

        group.MapPost("/import/apply", async (IFormFile file, ImportService import, CancellationToken ct) =>
        {
            if (file.Length is 0 or > Limits.ImportMaxBytes)
                throw new CmsException($"匯入檔大小須為 1 位元組至 {Limits.ImportMaxBytes} 位元組。");
            using var stream = file.OpenReadStream();
            return Results.Ok(await import.ApplyAsync(stream, confirmed: true, ct));
        }).DisableAntiforgery();
    }

    private static async Task<ImportService.ImportPreview> AnalyzeAsync(IFormFile file, ImportService import, CancellationToken ct)
    {
        if (file.Length is 0 or > Limits.ImportMaxBytes)
            throw new CmsException($"匯入檔大小須為 1 位元組至 {Limits.ImportMaxBytes} 位元組。");
        using var stream = file.OpenReadStream();
        return await import.AnalyzeAsync(stream, ct);
    }
}
