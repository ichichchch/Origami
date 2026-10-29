namespace Origami.Api;

/// <summary>
/// /api/v1/admin 的內容管理介面，接受 BFF 或綁定站點的外部憑證；
/// 每次操作由 Core 重驗使用者與實際權限。網站呈現／解鎖入口只接受 BFF，
/// 刪除等操作的人工確認仍由呼叫方取得。
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

        // 列表快速編輯：只改標題、代稱與草稿／待審／發布狀態；並發版本檢查與完整儲存相同。
        group.MapPut("/contents/{id}/quick", async (Guid id, QuickEditInput input, ContentService content, CancellationToken ct) =>
        {
            if (input.Status is not (ContentStatus.Draft or ContentStatus.Pending or ContentStatus.Published))
                return Results.BadRequest(new { message = "快速編輯僅接受草稿、待審或已發布狀態。" });
            return Results.Ok(await content.QuickEditAsync(id, input.Title, input.Slug, input.Status, input.Version, ct));
        });

        // 訪客張貼評論：端點僅接受 BFF 通行證（瀏覽器不持有任何憑證），
        // 服務層負責開關、內容可評性、蜜罐與基礎垃圾過濾；線上速率限制由網站主機補上。
        group.MapPost("/contents/{id}/comments", async (Guid id, CommentPostEnvelope input, CommentService comments, CancellationToken ct) =>
        {
            await comments.PostAsync(id, new CommentService.CommentPostInput(
                input.AuthorName ?? "", input.Url, input.Markdown ?? "", input.Honeypot == true), ct);
            return Results.NoContent();
        }).RequireAuthorization(ServiceTokenAuth.BffOnlyPolicy);

        // 評論審核佇列：狀態篩選、計數、審核與永久刪除。
        group.MapGet("/comments", async (HttpContext http, CommentService comments, CancellationToken ct) =>
        {
            if (!ApiEndpoints.TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error)) return error;
            if (!ApiEndpoints.TryQueryInt(http, "pageSize", Limits.PageSize, minimum: 1, maximum: Limits.MaxPageSize, out var pageSize, out error)) return error;
            CommentStatus? status = null;
            var rawStatus = http.Request.Query["status"].ToString();
            if (rawStatus.Length > 0)
            {
                if (!Enum.TryParse<CommentStatus>(rawStatus, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    return Results.BadRequest(new { message = "參數 status 無效。" });
                status = parsed;
            }
            return Results.Ok(await comments.ListAdminAsync(status, page ?? 1, pageSize ?? Limits.PageSize, ct));
        });

        group.MapGet("/comments/counts", async (CommentService comments, CancellationToken ct) =>
            Results.Ok(await comments.CountAdminAsync(ct)));

        group.MapPost("/comments/{id}/moderate", async (Guid id, Guid version, CommentModerateInput input, CommentService comments, CancellationToken ct) =>
        {
            if (!Enum.TryParse<CommentStatus>(input.Status, ignoreCase: true, out var target) || !Enum.IsDefined(target))
                return Results.BadRequest(new { message = "參數 status 無效。" });
            await comments.ModerateAsync(id, target, version, ct);
            return Results.NoContent();
        });

        group.MapDelete("/comments/{id}", async (Guid id, Guid version, CommentService comments, CancellationToken ct) =>
        {
            await comments.DeleteAsync(id, version, ct);
            return Results.NoContent();
        });

        // 編輯占用心跳：提示性占用，不阻止開啟或儲存；takeOver=true 明確接管他人仍有效的占用。
        group.MapPost("/contents/{id}/editing", async (Guid id, ContentService content, HttpContext http, CancellationToken ct) =>
        {
            var takeOver = http.Request.Query["takeOver"].ToString() is "true" or "1";
            return Results.Ok(await content.TouchEditingAsync(id, takeOver, ct));
        });

        // 離開編輯器時釋放自己的占用。
        group.MapDelete("/contents/{id}/editing", async (Guid id, ContentService content, CancellationToken ct) =>
        {
            await content.ReleaseEditingAsync(id, ct);
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
        }).RequireAuthorization(ServiceTokenAuth.BffOnlyPolicy);

        // 層級頁面網址的解析：多段路徑一次驗證父鏈，回傳內容與完整路徑段供轉址判斷。
        group.MapGet("/contents/published-page/{**path}", async (string path, ContentService content, CancellationToken ct) =>
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var resolved = await content.ResolvePublishedPagePathAsync(segments, ct);
            return resolved is null ? Results.NotFound() : Results.Ok(resolved);
        }).RequireAuthorization(ServiceTokenAuth.BffOnlyPolicy);

        // 解鎖 token 以自訂標頭遞送，避免查詢字串形式的憑證進入 URL 型日誌。
        group.MapGet("/contents/{id}/unlock-state", async (Guid id,
            [FromHeader(Name = "X-Unlock-Token")] string? token, ContentService content, CancellationToken ct) =>
            Results.Ok(new { unlocked = await content.IsUnlockedAsync(id, token, ct) }))
            .RequireAuthorization(ServiceTokenAuth.BffOnlyPolicy);

        group.MapPost("/contents/{id}/unlock", async (Guid id, ProtectionUnlockInput input, ContentService content, CancellationToken ct) =>
            Results.Ok(new { token = await content.VerifyProtectionAsync(id, input.Password, ct) }))
            .RequireAuthorization(ServiceTokenAuth.BffOnlyPolicy);

        // 瀏覽計數屬網站渲染的一部分；失敗不影響頁面。
        group.MapPost("/contents/{id}/view", async (Guid id, ContentService content, CancellationToken ct) =>
        {
            await content.RecordViewAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(ServiceTokenAuth.BffOnlyPolicy);
    }

    public sealed record ProtectionUnlockInput(string? Password);
    public sealed record QuickEditInput(string? Title, string? Slug, ContentStatus Status, Guid Version);
    public sealed record BulkContentInput(string? Action, List<BulkContentItem>? Items);
    public sealed record CommentPostEnvelope(string? AuthorName, string? Url, string? Markdown, bool? Honeypot);
    public sealed record CommentModerateInput(string Status);
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
