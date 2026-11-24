namespace Origami.Api;

/// <summary>
/// 自訂內容類型與自訂分類法的管理介面。內容類型為宣告式資料，不載入任何程式碼；
/// 權限由 Core 依實際角色與站點成員資格重驗。
/// </summary>
public static class ContentTypeEndpoints
{
    public static void MapContentTypeApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin").RequireAuthorization();

        group.MapGet("/content-types", async (ContentTypeService types, CancellationToken ct) =>
            Results.Ok(await types.ListAsync(ct)));

        group.MapPost("/content-types", async (ContentTypeService.TypeEdit input, ContentTypeService types,
            CancellationToken ct) =>
        {
            input.Id = null;
            return Results.Created("/api/v1/admin/content-types", await types.SaveAsync(input, ct));
        });

        group.MapPut("/content-types/{id}", async (Guid id, ContentTypeService.TypeEdit input,
            ContentTypeService types, CancellationToken ct) =>
        {
            input.Id = id;
            return Results.Ok(await types.SaveAsync(input, ct));
        });

        group.MapDelete("/content-types/{id}", async (Guid id, Guid version, ContentTypeService types,
            CancellationToken ct) =>
        {
            await types.DeleteAsync(id, version, ct);
            return Results.NoContent();
        });

        group.MapGet("/content-types/{id}/taxonomies", async (Guid id, ContentTypeService types,
            CancellationToken ct) => Results.Ok(await types.ListTaxonomiesAsync(id, ct)));

        group.MapPost("/content-types/{id}/taxonomies", async (Guid id, ContentTypeService.TaxonomyEdit input,
            ContentTypeService types, CancellationToken ct) =>
        {
            input.Id = null;
            input.ContentTypeId = id;
            return Results.Created($"/api/v1/admin/content-types/{id}/taxonomies",
                await types.SaveTaxonomyAsync(input, ct));
        });

        group.MapPut("/custom-taxonomies/{id}", async (Guid id, ContentTypeService.TaxonomyEdit input,
            ContentTypeService types, CancellationToken ct) =>
        {
            input.Id = id;
            return Results.Ok(await types.SaveTaxonomyAsync(input, ct));
        });

        group.MapDelete("/custom-taxonomies/{id}", async (Guid id, Guid version, ContentTypeService types,
            CancellationToken ct) =>
        {
            await types.DeleteTaxonomyAsync(id, version, ct);
            return Results.NoContent();
        });

        group.MapGet("/custom-taxonomies/{id}/terms", async (Guid id, ContentTypeService types,
            CancellationToken ct) => Results.Ok(await types.ListTermsAsync(id, ct)));

        group.MapPost("/custom-taxonomies/{id}/terms", async (Guid id, ContentTypeService.TermEdit input,
            ContentTypeService types, CancellationToken ct) =>
        {
            input.Id = null;
            input.TaxonomyId = id;
            return Results.Created($"/api/v1/admin/custom-taxonomies/{id}/terms",
                await types.SaveTermAsync(input, ct));
        });

        group.MapPut("/custom-terms/{id}", async (Guid id, ContentTypeService.TermEdit input,
            ContentTypeService types, CancellationToken ct) =>
        {
            input.Id = id;
            return Results.Ok(await types.SaveTermAsync(input, ct));
        });

        group.MapDelete("/custom-terms/{id}", async (Guid id, Guid version, ContentTypeService types,
            CancellationToken ct) =>
        {
            await types.DeleteTermAsync(id, version, ct);
            return Results.NoContent();
        });

        // 內容編輯器需要的可選詞彙：依內容類型列出其分類法與詞彙。
        group.MapGet("/content-types/{id}/terms", async (Guid id, ContentTypeService types,
            CancellationToken ct) => Results.Ok(await types.TermsForContentTypeAsync(id, ct)));

        // 自訂欄位：受控的宣告式擴充點，僅能以資料宣告欄位，不載入程式碼。
        group.MapGet("/content-types/{id}/fields", async (Guid id, ContentTypeService types,
            CancellationToken ct) => Results.Ok(await types.ListFieldsAsync(id, ct)));

        group.MapPost("/content-types/{id}/fields", async (Guid id, ContentTypeService.FieldEdit input,
            ContentTypeService types, CancellationToken ct) =>
        {
            input.Id = null;
            input.ContentTypeId = id;
            return Results.Created($"/api/v1/admin/content-types/{id}/fields",
                await types.SaveFieldAsync(input, ct));
        });

        // 內建文章／頁面的自訂欄位：不屬於任何內容類型，以內容種類（post／page）區分；更新與刪除沿用 /custom-fields/{id}。
        group.MapGet("/builtin-content/{kind}/fields", async (string kind, ContentTypeService types, CancellationToken ct) =>
            TryBuiltinKind(kind, out var builtin)
                ? Results.Ok(await types.ListBuiltinFieldsAsync(builtin!.Value, ct))
                : Results.BadRequest(new { message = "參數 kind 無效（post 或 page）。" }));

        group.MapPost("/builtin-content/{kind}/fields", async (string kind, ContentTypeService.FieldEdit input,
            ContentTypeService types, CancellationToken ct) =>
        {
            if (!TryBuiltinKind(kind, out var builtin))
                return Results.BadRequest(new { message = "參數 kind 無效（post 或 page）。" });
            input.Id = null;
            input.ContentTypeId = null;
            input.BuiltinKind = builtin!.Value;
            return Results.Created($"/api/v1/admin/builtin-content/{kind}/fields", await types.SaveFieldAsync(input, ct));
        });

        group.MapPut("/custom-fields/{id}", async (Guid id, ContentTypeService.FieldEdit input,
            ContentTypeService types, CancellationToken ct) =>
        {
            input.Id = id;
            return Results.Ok(await types.SaveFieldAsync(input, ct));
        });

        group.MapDelete("/custom-fields/{id}", async (Guid id, Guid version, ContentTypeService types,
            CancellationToken ct) =>
        {
            await types.DeleteFieldAsync(id, version, ct);
            return Results.NoContent();
        });
    }

    private static bool TryBuiltinKind(string? kind, out ContentKind? parsed)
    {
        parsed = Enum.TryParse<ContentKind>(kind ?? "", ignoreCase: true, out var value) && value is ContentKind.Post or ContentKind.Page
            ? value
            : null;
        return parsed is not null;
    }
}
