namespace Origami.Api;

/// <summary>在認證前以明確站點識別隔離 API／MCP；不信任 Host 或代理標頭作為授權來源。</summary>
public sealed class SiteResolutionMiddleware(RequestDelegate next)
{
    internal static bool IsTenantPath(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        // 健康檢查及其他非租戶路徑不解析站點，也不因站點目錄不可用而提早失敗。
        if (!IsTenantPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var siteId = SiteDefaults.DefaultSiteId;
        if (context.Request.Headers.TryGetValue(AccessClaims.SiteHeader, out var values) &&
            (values.Count != 1 || !Guid.TryParse(values[0], out siteId)))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "站點標頭須為單一有效 GUID。" }, context.RequestAborted);
            return;
        }

        var factory = context.RequestServices.GetRequiredService<IDbContextFactory<CmsDbContext>>();
        await using (var db = await factory.CreateDbContextAsync(context.RequestAborted))
        {
            // Sites 是全域目錄；不依賴尚未設定的 CurrentSiteId，也不能選到停用的站點。
            if (!await db.Sites.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(site => site.Id == siteId && site.IsActive, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { message = "站點不存在或已停用。" }, context.RequestAborted);
                return;
            }
        }
        context.RequestServices.GetRequiredService<SiteContext>().SiteId = siteId;
        // resolve 也允許省略站點標頭；實際 host 對應完全由公開解析服務負責。
        await next(context);
    }
}
