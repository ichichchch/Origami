namespace Origami.Api;

/// <summary>
/// BFF 寫入介面上的媒體庫。上傳檔案落入網站在 /media 提供服務的同一個持久化
/// 儲存目錄；本主機本身從不提供檔案內容。
/// </summary>
public static class AdminMediaEndpoints
{
    public static void MapAdminMediaApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/media").RequireAuthorization();

        group.MapGet("", async (HttpContext http, MediaService media, CancellationToken ct) =>
        {
            if (!ApiEndpoints.TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error)) return error;
            if (!ApiEndpoints.TryQueryInt(http, "pageSize", Limits.PageSize, minimum: 1, maximum: Limits.MaxPageSize, out var pageSize, out error)) return error;
            var search = http.Request.Query["search"].ToString();
            return Results.Ok(await media.ListAsync(page ?? 1, pageSize ?? Limits.PageSize, ct, search.Length == 0 ? null : search));
        });

        group.MapPost("", async (IFormFile file, MediaService media, CancellationToken ct) =>
        {
            // 便宜的第一道閘門；服務端仍會重新驗證實際格式、大小與可解碼性。
            if (file.Length is 0 or > Limits.UploadBytes)
                return Results.BadRequest(new { message = $"圖片大小須為 1 位元組至 {Limits.UploadBytes} 位元組。" });
            using var stream = file.OpenReadStream();
            var item = await media.UploadAsync(stream, file.FileName, ct);
            return Results.Created($"/api/v1/admin/media/{item.Id}", item);
        }).DisableAntiforgery(); // BFF 通行證即認證；表單端點明確退出瀏覽器防偽機制

        group.MapPut("/{id}/alt", async (Guid id, AltTextInput input, MediaService media, CancellationToken ct) =>
            Results.Ok(await media.UpdateAltAsync(id, input.AltText, ct)));

        // 引用資訊支撐刪除確認 UI：媒體仍在使用中時拒絕刪除。
        group.MapGet("/{id}/references", async (Guid id, MediaService media, CancellationToken ct) =>
            Results.Ok(await media.ListReferencesAsync(id, ct)));

        group.MapDelete("/{id}", async (Guid id, bool confirmed, MediaService media, CancellationToken ct) =>
        {
            if (!confirmed) return Results.BadRequest(new { message = "請先確認刪除媒體。" });
            await media.DeleteAsync(id, confirmed, ct);
            return Results.NoContent();
        });
    }

    public sealed record AltTextInput(string? AltText);
}
