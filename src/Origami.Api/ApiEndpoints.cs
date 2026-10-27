namespace Origami.Api;

/// <summary>
/// Read-only JSON API v1 for native and mobile clients. It mirrors the MCP read surface:
/// only published content, drafts/protected/private items are never returned.
/// </summary>
public static class ApiEndpoints
{
    public static void MapApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1");

        group.MapGet("/posts", async (ContentService content, HttpContext http, CancellationToken ct) =>
        {
            if (!TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error)) return error;
            if (!TryQueryInt(http, "pageSize", Limits.PageSize, minimum: 1, maximum: Limits.MaxPageSize, out var pageSize, out error)) return error;
            string? category = http.Request.Query["category"].Count > 0 ? http.Request.Query["category"].ToString() : null;
            string? tag = http.Request.Query["tag"].Count > 0 ? http.Request.Query["tag"].ToString() : null;
            if (!TryQueryInt(http, "year", null, minimum: 1, maximum: 9999, out var year, out error)) return error;
            if (!TryQueryInt(http, "month", null, minimum: 1, maximum: 12, out var month, out error)) return error;
            return Results.Ok(await content.ListPublishedAsync(page ?? 1, pageSize ?? Limits.PageSize, ct, category, tag, year, month));
        });

        group.MapGet("/posts/{slug}", async (string slug, ContentService content, CancellationToken ct) =>
            PublishedOrNotFound(await content.GetPublishedAsync(ContentKind.Post, slug, ct)));

        group.MapGet("/pages/{slug}", async (string slug, ContentService content, CancellationToken ct) =>
            PublishedOrNotFound(await content.GetPublishedAsync(ContentKind.Page, slug, ct)));

        group.MapGet("/search", async (string? q, int? limit, PublicSearchService search, CancellationToken ct) =>
        {
            var query = (q ?? "").Trim();
            if (query.Length == 0) return Results.BadRequest(new { message = "請輸入搜尋文字。" });
            if (query.Length > Limits.SearchQueryMax) return Results.BadRequest(new { message = "搜尋文字過長。" });
            // Keyword matching keeps working without an embedding provider; the semantic leg degrades.
            return Results.Ok(await search.SearchAsync(query, limit ?? Limits.PageSize, ct));
        });
    }

    /// <summary>Password-protected and private items are invisible to machine clients, exactly like MCP tools.</summary>
    private static IResult PublishedOrNotFound(PublicContent? found) =>
        found is { IsProtected: false, IsPrivate: false } ? Results.Ok(found) : Results.NotFound();

    private static bool TryQueryInt(HttpContext http, string key, int? defaultValue, int minimum, int maximum,
        out int? value, out IResult error)
    {
        // Absent means "no preference": fall back to the default, which stays null for optional filters.
        value = defaultValue;
        error = Results.Empty;
        var raw = http.Request.Query[key];
        if (raw.Count == 0) return true;
        if (!int.TryParse(raw, out var parsed) || parsed < minimum || parsed > maximum)
        {
            error = Results.BadRequest(new { message = $"參數 {key} 無效。" });
            return false;
        }
        value = parsed;
        return true;
    }
}
