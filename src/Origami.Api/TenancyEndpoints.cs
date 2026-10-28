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
    }
}
