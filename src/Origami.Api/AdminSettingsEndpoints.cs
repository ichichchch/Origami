namespace Origami.Api;

/// <summary>BFF 寫入介面上的單站點設定。</summary>
public static class AdminSettingsEndpoints
{
    public static void MapAdminSettingsApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/settings").RequireAuthorization();

        group.MapGet("", async (SettingsService settings, AdminAccess access, CancellationToken ct) =>
        {
            // GetAsync 也用於公開設定投影；管理端不能沿用其匿名讀取權限。
            await access.RequireRoleAsync(SiteRole.Administrator, ct);
            return Results.Ok(await settings.GetAsync(ct));
        });

        group.MapPut("", async (SiteSettings input, SettingsService settings, CancellationToken ct) =>
            Results.Ok(await settings.SaveAsync(input, ct)));
    }
}
