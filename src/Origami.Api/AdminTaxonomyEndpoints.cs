namespace Origami.Api;

/// <summary>BFF 寫入介面上的分類與標籤管理。</summary>
public static class AdminTaxonomyEndpoints
{
    public static void MapAdminTaxonomyApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/taxonomies").RequireAuthorization();

        group.MapGet("", async (HttpContext http, TaxonomyService taxonomy, CancellationToken ct) =>
        {
            if (!TryKind(http, out var kind, out var kindError)) return kindError;
            if (!ApiEndpoints.TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error)) return error;
            if (!ApiEndpoints.TryQueryInt(http, "pageSize", Limits.MaxPageSize, minimum: 1, maximum: Limits.MaxPageSize, out var pageSize, out error)) return error;
            return Results.Ok(await taxonomy.ListAsync(kind!.Value, page ?? 1, pageSize ?? Limits.MaxPageSize, ct));
        });

        group.MapPost("", async (HttpContext http, TaxonomyEdit input, TaxonomyService taxonomy, CancellationToken ct) =>
        {
            if (!TryKind(http, out var kind, out var kindError)) return kindError;
            input.Id = null; // 建立絕不得指向既有資料列
            return Results.Ok(await taxonomy.SaveAsync(kind!.Value, input, ct));
        });

        group.MapPut("/{id}", async (Guid id, HttpContext http, TaxonomyEdit input, TaxonomyService taxonomy, CancellationToken ct) =>
        {
            if (!TryKind(http, out var kind, out var kindError)) return kindError;
            input.Id = id; // 身分識別由路由決定
            return Results.Ok(await taxonomy.SaveAsync(kind!.Value, input, ct));
        });

        group.MapDelete("/{id}", async (Guid id, Guid version, HttpContext http, TaxonomyService taxonomy, CancellationToken ct) =>
        {
            if (!TryKind(http, out var kind, out var kindError)) return kindError;
            await taxonomy.DeleteAsync(kind!.Value, id, version, ct);
            return Results.NoContent();
        });

        // 合併：來源的關聯全部改指到目標後刪除來源；version 為來源的並發版本。
        group.MapPost("/{id}/merge", async (Guid id, TaxonomyMergeInput input, HttpContext http, TaxonomyService taxonomy, CancellationToken ct) =>
        {
            if (!TryKind(http, out var kind, out var kindError)) return kindError;
            var moved = await taxonomy.MergeAsync(kind!.Value, id, input.TargetId, input.Version, ct);
            return Results.Ok(new { moved });
        });

        // 批次刪除：逐筆攜帶並發版本、逐筆回報；確認契約與單筆刪除相同（由 BFF 呼叫方取得人工確認）。
        group.MapPost("/bulk-delete", async (BulkTaxonomyDeleteInput input, TaxonomyService taxonomy, CancellationToken ct) =>
        {
            if (!Enum.IsDefined(input.Kind)) return Results.BadRequest(new { message = "參數 kind 無效（category 或 tag）。" });
            var items = (input.Items ?? []).Where(x => x is not null).Select(x => (x.Id, x.Version)).ToList();
            return Results.Ok(await taxonomy.BulkDeleteAsync(input.Kind, items, ct));
        });
    }

    private static bool TryKind(HttpContext http, out TaxonomyKind? kind, out IResult? error)
    {
        error = null;
        kind = null;
        var raw = http.Request.Query["kind"].ToString();
        if (raw.Length == 0 || !Enum.TryParse<TaxonomyKind>(raw, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            error = Results.BadRequest(new { message = "參數 kind 無效（category 或 tag）。" });
            return false;
        }
        kind = parsed;
        return true;
    }

    public sealed record TaxonomyMergeInput(Guid TargetId, Guid Version);
    public sealed record BulkTaxonomyDeleteInput(TaxonomyKind Kind, List<BulkVersionedItem>? Items);
    public sealed record BulkVersionedItem(Guid Id, Guid Version);
}
