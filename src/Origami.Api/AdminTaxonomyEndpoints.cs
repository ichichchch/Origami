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
}
