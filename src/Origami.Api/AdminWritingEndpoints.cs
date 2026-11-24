namespace Origami.Api;

/// <summary>內容範本與可重複片段的管理介面；內容為 Markdown，渲染時與一般內容走同一條安全管線。</summary>
public static class AdminWritingEndpoints
{
    public static void MapAdminWritingApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin").RequireAuthorization();

        group.MapGet("/templates", async (WritingAssetsService assets, CancellationToken ct) =>
            Results.Ok(await assets.ListTemplatesAsync(ct)));

        group.MapPost("/templates", async (WritingAssetsService.TemplateEdit input, WritingAssetsService assets,
            CancellationToken ct) =>
        {
            input.Id = null; // 建立絕不得指向既有資料列
            return Results.Created("/api/v1/admin/templates", await assets.SaveTemplateAsync(input, ct));
        });

        group.MapPut("/templates/{id}", async (Guid id, WritingAssetsService.TemplateEdit input, WritingAssetsService assets,
            CancellationToken ct) =>
        {
            input.Id = id; // 身分識別由路由決定
            return Results.Ok(await assets.SaveTemplateAsync(input, ct));
        });

        group.MapDelete("/templates/{id}", async (Guid id, Guid version, WritingAssetsService assets, CancellationToken ct) =>
        {
            await assets.DeleteTemplateAsync(id, version, ct);
            return Results.NoContent();
        });

        group.MapGet("/snippets", async (WritingAssetsService assets, CancellationToken ct) =>
            Results.Ok(await assets.ListSnippetsAsync(ct)));

        group.MapPost("/snippets", async (WritingAssetsService.SnippetEdit input, WritingAssetsService assets,
            CancellationToken ct) =>
        {
            input.Id = null;
            return Results.Created("/api/v1/admin/snippets", await assets.SaveSnippetAsync(input, ct));
        });

        group.MapPut("/snippets/{id}", async (Guid id, WritingAssetsService.SnippetEdit input, WritingAssetsService assets,
            CancellationToken ct) =>
        {
            input.Id = id;
            return Results.Ok(await assets.SaveSnippetAsync(input, ct));
        });

        group.MapDelete("/snippets/{id}", async (Guid id, Guid version, WritingAssetsService assets, CancellationToken ct) =>
        {
            await assets.DeleteSnippetAsync(id, version, ct);
            return Results.NoContent();
        });
    }
}
