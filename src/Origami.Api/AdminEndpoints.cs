using Microsoft.AspNetCore.Mvc;

namespace Origami.Api;

/// <summary>
/// /api/v1/admin 下的 BFF 寫入介面，以共用服務 token 授權。網站主機
/// 代表管理員呼叫這些端點前，會自行完成 cookie＋兩步驗證檢查；
/// 服務中的 confirmed 旗標即由這項呼叫方契約滿足。
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin").RequireAuthorization();

        group.MapGet("/contents", async (ContentService content, HttpContext http, CancellationToken ct) =>
        {
            var kind = ParseKind(http.Request.Query["kind"].ToString(), fallback: ContentKind.Post, out var kindError);
            if (kindError is not null) return kindError;
            if (!ApiEndpoints.TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error)) return error;
            if (!ApiEndpoints.TryQueryInt(http, "pageSize", Limits.PageSize, minimum: 1, maximum: Limits.MaxPageSize, out var pageSize, out error)) return error;
            ContentStatus? status = null;
            var rawStatus = http.Request.Query["status"].ToString();
            if (rawStatus.Length > 0)
            {
                if (!Enum.TryParse<ContentStatus>(rawStatus, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    return Results.BadRequest(new { message = "參數 status 無效。" });
                status = parsed;
            }
            var search = http.Request.Query["search"].ToString();
            return Results.Ok(await content.ListAdminAsync(kind!.Value, page ?? 1, pageSize ?? Limits.PageSize, ct,
                status, search.Length == 0 ? null : search));
        });

        group.MapGet("/contents/{id}", async (Guid id, ContentService content, CancellationToken ct) =>
            Results.Ok(await content.GetEditAsync(id, ct)));

        group.MapPost("/contents", async (ContentEdit input, ContentService content, CancellationToken ct) =>
        {
            input.Id = null; // 建立操作絕不能命中既有資料列，無論請求主體聲稱為何
            if (!Enum.IsDefined(input.Kind)) return Results.BadRequest(new { message = "內容類型無效。" });
            var created = await content.CreateAsync(input.Kind, input, ct);
            return Results.Created($"/api/v1/admin/contents/{created.Id}", created);
        });

        group.MapPut("/contents/{id}", async (Guid id, ContentEdit input, ContentService content, CancellationToken ct) =>
        {
            input.Id = id; // 身份由路由決定；請求主體中不符的 id 予以忽略
            return Results.Ok(await content.UpdateAsync(input, ct));
        });

        group.MapDelete("/contents/{id}", async (Guid id, Guid version, ContentService content, bool permanent = false, CancellationToken ct = default) =>
        {
            // 呼叫方（網站 UI）負責取得人工確認；端點只負責執行。
            if (permanent)
            {
                await content.DeleteAsync(id, version, confirmed: true, ct);
                return Results.NoContent();
            }
            await content.TrashAsync(id, version, confirmed: true, ct);
            return Results.NoContent();
        });

        group.MapPost("/contents/{id}/restore", async (Guid id, Guid version, ContentService content, CancellationToken ct) =>
        {
            await content.RestoreAsync(id, version, ct);
            return Results.NoContent();
        });

        // 清單批次操作：逐筆攜帶並發版本，逐筆回報結果；確認契約與單筆端點相同（由 BFF 呼叫方滿足）。
        group.MapPost("/contents/bulk", async (BulkContentInput input, ContentService content, CancellationToken ct) =>
        {
            if (!Enum.TryParse<BulkAction>(input.Action, ignoreCase: true, out var action) || !Enum.IsDefined(action))
                return Results.BadRequest(new { message = "參數 action 無效。" });
            if (input.Items is null || input.Items.Count == 0)
                return Results.BadRequest(new { message = "請先選擇要操作的內容。" });
            if (input.Items.Count > Limits.BulkMaxItems)
                return Results.BadRequest(new { message = $"單次批次操作最多 {Limits.BulkMaxItems} 筆。" });
            var results = await content.BulkAsync(action, [.. input.Items.Select(x => (x.Id, x.Version))], ct);
            return Results.Ok(results);
        });

        group.MapGet("/contents/counts", async (HttpContext http, ContentService content, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ContentKind>(http.Request.Query["kind"].ToString(), ignoreCase: true, out var kind)
                || !Enum.IsDefined(kind))
                return Results.BadRequest(new { message = "參數 kind 無效。" });
            return Results.Ok(await content.CountAdminAsync(kind, ct));
        });

        group.MapGet("/contents/{id}/revisions", async (Guid id, ContentService content, CancellationToken ct) =>
            Results.Ok(await content.ListRevisionsAsync(id, ct)));

        group.MapGet("/revisions/{id}", async (Guid id, ContentService content, CancellationToken ct) =>
            await content.GetRevisionAsync(id, ct) is { } detail ? Results.Ok(detail) : Results.NotFound());

        // 預覽 token 不屬於編輯中的內容：輪替 token 不會改變並發版本。
        group.MapPost("/contents/{id}/preview-token", async (Guid id, ContentService content, CancellationToken ct) =>
            Results.Ok(new { token = await content.CyclePreviewTokenAsync(id, ct) }));

        group.MapDelete("/contents/{id}/preview-token", async (Guid id, ContentService content, CancellationToken ct) =>
        {
            await content.RevokePreviewTokenAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/pages/options", async (ContentService content, CancellationToken ct) =>
            Results.Ok(await content.PageOptionsAsync(ct)));

        // 網站渲染用的完整詳情：機器端點會隱藏受保護與私密內容，網站需要旗標與本文才能呈現解鎖閘門。
        group.MapGet("/contents/published/{kind}/{slug}", async (string kind, string slug, ContentService content, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ContentKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return Results.BadRequest(new { message = "參數 kind 無效。" });
            return await content.GetPublishedAsync(parsed, slug, ct) is { } detail
                ? Results.Ok(detail)
                : Results.NotFound();
        });

        // 層級頁面網址的解析：多段路徑一次驗證父鏈，回傳內容與完整路徑段供轉址判斷。
        group.MapGet("/contents/published-page/{**path}", async (string path, ContentService content, CancellationToken ct) =>
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var resolved = await content.ResolvePublishedPagePathAsync(segments, ct);
            return resolved is null ? Results.NotFound() : Results.Ok(resolved);
        });

        // 解鎖 token 以自訂標頭遞送，避免查詢字串形式的憑證進入 URL 型日誌。
        group.MapGet("/contents/{id}/unlock-state", async (Guid id,
            [FromHeader(Name = "X-Unlock-Token")] string? token, ContentService content, CancellationToken ct) =>
            Results.Ok(new { unlocked = await content.IsUnlockedAsync(id, token, ct) }));

        group.MapPost("/contents/{id}/unlock", async (Guid id, ProtectionUnlockInput input, ContentService content, CancellationToken ct) =>
            Results.Ok(new { token = await content.VerifyProtectionAsync(id, input.Password, ct) }));

        // 瀏覽計數屬網站渲染的一部分；失敗不影響頁面。
        group.MapPost("/contents/{id}/view", async (Guid id, ContentService content, CancellationToken ct) =>
        {
            await content.RecordViewAsync(id, ct);
            return Results.NoContent();
        });
    }

    public sealed record ProtectionUnlockInput(string? Password);
    public sealed record BulkContentInput(string? Action, List<BulkContentItem>? Items);
    public sealed record BulkContentItem(Guid Id, Guid Version);

    private static ContentKind? ParseKind(string raw, ContentKind fallback, out IResult? error)
    {
        error = null;
        if (raw.Length == 0) return fallback;
        if (Enum.TryParse<ContentKind>(raw, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        error = Results.BadRequest(new { message = "參數 kind 無效。" });
        return null;
    }
}
