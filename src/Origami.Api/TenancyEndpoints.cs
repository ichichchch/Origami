namespace Origami.Api;

/// <summary>公開主機名稱解析，以及僅供 BFF 使用的站點、帳號、成員與外部憑證管理。</summary>
public static class TenancyEndpoints
{
    public static void MapTenancyApiV1(this WebApplication app)
    {
        // 僅回傳公開站點中繼資料，不回傳設定、成員或任何登入／憑證資訊。
        app.MapGet("/api/v1/sites/resolve", async (string host, SiteAdministrationService sites, CancellationToken ct) =>
            await sites.ResolveHostAsync(host, ct) is { } site ? Results.Ok(site) : Results.NotFound())
            .AllowAnonymous();

        // PAT 即使有寫入範圍或全站管理者身分，也不能使用管理身分／憑證的入口。
        // 服務負責各項操作的 network/site 角色檢查，GET 的 403 不降級為匿名結果。
        var group = app.MapGroup("/api/v1/admin").RequireAuthorization(ServiceTokenAuth.BffOnlyPolicy);

        // 受控導覽管理面：清單、建立／更新、刪除與排序。
        group.MapGet("/navigation", async (NavigationService navigation, CancellationToken ct) =>
            Results.Ok(await navigation.ListAsync(ct)));

        group.MapPost("/navigation", async (NavigationService.NavItemInput input, NavigationService navigation, CancellationToken ct) =>
        {
            input.Id = null; // 建立絕不得指向既有資料列
            return Results.Ok(await navigation.SaveAsync(input, ct));
        });

        group.MapPut("/navigation/{id}", async (Guid id, NavigationService.NavItemInput input, NavigationService navigation, CancellationToken ct) =>
        {
            input.Id = id; // 身分識別由路由決定
            return Results.Ok(await navigation.SaveAsync(input, ct));
        });

        group.MapDelete("/navigation/{id}", async (Guid id, Guid version, NavigationService navigation, CancellationToken ct) =>
        {
            await navigation.DeleteAsync(id, version, ct);
            return Results.NoContent();
        });

        // 排序：逐筆攜帶版本，依提交順序重新編號。
        group.MapPut("/navigation/order", async (NavigationOrderInput input, NavigationService navigation, CancellationToken ct) =>
            Results.Ok(await navigation.ReorderAsync([.. (input.Items ?? []).Select(x => (x.Id, x.Version))], ct)));

        group.MapGet("/access", async (SiteAdministrationService sites, CancellationToken ct) =>
            Results.Ok(await sites.GetAccessAsync(ct)));

        group.MapGet("/sites", async (SiteAdministrationService sites, CancellationToken ct) =>
            Results.Ok(await sites.ListSitesAsync(ct)));

        group.MapPost("/sites", async (SiteCreateInput input, SiteAdministrationService sites, CancellationToken ct) =>
        {
            var created = await sites.CreateSiteAsync(input, ct);
            return Results.Created($"/api/v1/admin/sites/{created.Id}", created);
        });

        group.MapPut("/sites/{id}", async (Guid id, SiteUpdateInput input, SiteAdministrationService sites, CancellationToken ct) =>
            Results.Ok(await sites.UpdateSiteAsync(id, input, ct)));

        group.MapGet("/users", async (HttpContext http, SiteAdministrationService sites, CancellationToken ct) =>
        {
            if (!ApiEndpoints.TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error))
                return error;
            var search = http.Request.Query["search"].ToString();
            return Results.Ok(await sites.ListUsersAsync(page ?? 1, search.Length == 0 ? null : search, ct));
        });

        // 密碼僅從 JSON 主體繫結；Program 先檢查 HTTPS／同機 BFF 通道，回應僅含 UserSummary。
        group.MapPost("/users", async (UserCreateInput input, SiteAdministrationService sites, CancellationToken ct) =>
        {
            var created = await sites.CreateUserAsync(input, ct);
            return Results.Created($"/api/v1/admin/users/{Uri.EscapeDataString(created.Id)}", created);
        });

        group.MapPut("/users/{id}/state", async (string id, UserStateInput input, SiteAdministrationService sites, CancellationToken ct) =>
        {
            await sites.SetUserDisabledAsync(id, input.IsDisabled, ct);
            return Results.NoContent();
        });

        group.MapPost("/users/{id}/reset-2fa", async (string id, SiteAdministrationService sites, CancellationToken ct) =>
        {
            await sites.ResetUserTwoFactorAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/members", async (SiteAdministrationService sites, CancellationToken ct) =>
            Results.Ok(await sites.ListMembersAsync(ct)));

        group.MapPut("/members", async (MemberEditInput input, SiteAdministrationService sites, CancellationToken ct) =>
        {
            await sites.SetMemberAsync(input, ct);
            return Results.NoContent();
        });

        group.MapDelete("/members/{userId}", async (string userId, Guid version, SiteAdministrationService sites, CancellationToken ct) =>
        {
            await sites.RemoveMemberAsync(userId, version, ct);
            return Results.NoContent();
        });

        group.MapGet("/credentials", async (ApiCredentialService credentials, CancellationToken ct) =>
            Results.Ok(await credentials.ListAsync(ct)));

        group.MapPost("/credentials", async (CredentialCreateInput input, ApiCredentialService credentials, CancellationToken ct) =>
        {
            var created = await credentials.CreateAsync(input, ct);
            // Token 僅在這次成功回應出現，沿用主機的 no-store 回應政策。
            return Results.Created($"/api/v1/admin/credentials/{created.Id}", created);
        });

        group.MapDelete("/credentials/{id}", async (Guid id, ApiCredentialService credentials, CancellationToken ct) =>
        {
            await credentials.RevokeAsync(id, ct);
            return Results.NoContent();
        });

        // 帳號自助個人資料與密碼修改（僅 BFF）。
        group.MapGet("/profile", async (SiteAdministrationService sites, CancellationToken ct) =>
            Results.Ok(await sites.GetMyProfileAsync(ct)));

        group.MapPut("/profile", async (AccountProfileEdit input, SiteAdministrationService sites, CancellationToken ct) =>
            Results.Ok(await sites.UpdateMyProfileAsync(input, ct)));

        group.MapPost("/profile/password", async (ChangePasswordInput input, SiteAdministrationService sites, CancellationToken ct) =>
        {
            await sites.ChangeMyPasswordAsync(input, ct);
            return Results.NoContent();
        });
    }

    public sealed record NavigationOrderInput(List<NavigationOrderItem>? Items);
    public sealed record NavigationOrderItem(Guid Id, Guid Version);
}
