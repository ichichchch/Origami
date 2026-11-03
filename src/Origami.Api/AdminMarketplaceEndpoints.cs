namespace Origami.Api;

/// <summary>受控包市場管理面：內建目錄、已安裝清單、ZIP 解析／安裝、啟用停用與解除；僅供 BFF 使用。</summary>
public static class AdminMarketplaceEndpoints
{
    public static void MapAdminMarketplaceApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/marketplace").RequireAuthorization(ServiceTokenAuth.BffOnlyPolicy);

        // 內建目錄與各站安裝狀態；已安裝包的完整檢視另有 /packages。
        group.MapGet("/catalog", async (MarketplaceService marketplace, CancellationToken ct) =>
            Results.Ok(await marketplace.CatalogAsync(ct)));

        group.MapGet("/packages", async (MarketplaceService marketplace, CancellationToken ct) =>
            Results.Ok(await marketplace.InstalledAsync(ct)));

        group.MapPost("/packages/from-catalog", async (CatalogInstallInput input, MarketplaceService marketplace, CancellationToken ct) =>
            Results.Ok(await marketplace.InstallFromCatalogAsync(input.Slug ?? "", ct)));

        // ZIP 解析只預覽不落地；由網站 UI 先呈現內容摘要再決定安裝。
        group.MapPost("/packages/analyze", async (IFormFile file, MarketplaceService marketplace, CancellationToken ct) =>
        {
            using var stream = OpenPackageFile(file);
            return Results.Ok(await marketplace.AnalyzeZipAsync(stream, ct));
        }).DisableAntiforgery();

        group.MapPost("/packages/install-upload", async (IFormFile file, MarketplaceService marketplace, CancellationToken ct) =>
        {
            using var stream = OpenPackageFile(file);
            return Results.Ok(await marketplace.InstallFromZipAsync(stream, ct));
        }).DisableAntiforgery();

        group.MapPut("/packages/{id}/enabled", async (Guid id, PackageEnableInput input, MarketplaceService marketplace, CancellationToken ct) =>
            Results.Ok(await marketplace.SetEnabledAsync(id, input.IsEnabled, input.Version, ct)));

        // 第三方（WordPress）主題相容匯入：只讀取宣告式中繼資料與可推導的視覺參數，模板程式碼不保留。
        group.MapPost("/packages/import-theme/analyze", async (IFormFile file, MarketplaceService marketplace, CancellationToken ct) =>
        {
            using var stream = OpenThemeFile(file);
            return Results.Ok(await marketplace.AnalyzeWordPressThemeAsync(stream, ct));
        }).DisableAntiforgery();

        group.MapPost("/packages/import-theme", async (IFormFile file, MarketplaceService marketplace, CancellationToken ct) =>
        {
            using var stream = OpenThemeFile(file);
            return Results.Ok(await marketplace.InstallWordPressThemeAsync(stream, ct));
        }).DisableAntiforgery();

        // 匯出為可分享的 ZIP（僅 manifest.json），供其他站點匯入。
        group.MapGet("/packages/{id}/export", async (Guid id, Guid version, MarketplaceService marketplace, CancellationToken ct) =>
            Results.File(await marketplace.ExportAsync(id, version, ct), "application/zip", "origami-package.zip"));

        group.MapDelete("/packages/{id}", async (Guid id, Guid version, MarketplaceService marketplace, CancellationToken ct) =>
        {
            await marketplace.UninstallAsync(id, version, ct);
            return Results.NoContent();
        });
    }

    private static Stream OpenThemeFile(IFormFile file)
    {
        if (file.Length is 0 or > Limits.WordPressThemeZipMaxBytes)
            throw new CmsException($"主題封裝大小須為 1 位元組至 {Limits.WordPressThemeZipMaxBytes} 位元組。");
        return file.OpenReadStream();
    }

    private static Stream OpenPackageFile(IFormFile file)
    {
        // 端點第一道的便宜閘門；服務層仍有解壓量與內容驗證的完整防護。
        if (file.Length is 0 or > Limits.PackageZipMaxBytes)
            throw new CmsException($"包檔大小須為 1 位元組至 {Limits.PackageZipMaxBytes} 位元組。");
        return file.OpenReadStream();
    }

    public sealed record CatalogInstallInput(string? Slug);
    public sealed record PackageEnableInput(bool IsEnabled, Guid Version);
}
