namespace Origami.Tests;

/// <summary>對隔離的 Api 主機驅動 BFF 寫入面 /api/v1/admin。</summary>
[Collection(PostgresCollection.Name)]
public sealed class ApiV1AdminEndpointTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Token = "isolated-test-service-token-0123456789abcdef";

    private async Task<HttpClient> CreateAdminClientAsync(RunningApi api, string stamp)
    {
        return api.CreateBffClient(Token, await App.AdminUserIdAsync(), stamp);
    }

    [PostgresFact]
    public async Task Write_endpoints_stay_closed_without_a_valid_service_token()
    {
        // 未設定 token 時，寫入面必須全部拒絕，包括匿名讀取管理資料。
        await using (var closed = new RunningApi(App.ConnectionString))
        {
            await closed.StartAsync();
            using var client = closed.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/v1/admin/contents?kind=Post")).StatusCode);
        }

        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var noToken = api.CreateClient();
        using var wrongToken = api.CreateClient();
        wrongToken.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-token");
        foreach (var client in new[] { noToken, wrongToken })
        {
            using var post = await client.PostAsJsonAsync("/api/v1/admin/contents",
                new { title = "不該建立", kind = "Post", markdown = "x", status = "Draft" });
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
            using var list = await client.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        }

        // 通行證正確、但工作階段戳記缺失或已輪替（登出／密碼重設後的舊電路）：
        // 管理寫入仍必須被拒——這是 BFF 架構下立即撤銷互動電路寫入權的防線。
        using var noStamp = api.CreateClient();
        noStamp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        noStamp.DefaultRequestHeaders.Add(AccessClaims.UserHeader, await App.AdminUserIdAsync());
        using var staleStamp = api.CreateBffClient(Token, await App.AdminUserIdAsync(), "rotated-away-stamp");
        using var noUser = api.CreateClient();
        noUser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        noUser.DefaultRequestHeaders.Add(AccessClaims.StampHeader, await App.AdminSecurityStampAsync());
        foreach (var client in new[] { noStamp, staleStamp, noUser })
        {
            using var post = await client.PostAsJsonAsync("/api/v1/admin/contents",
                new { title = "不該建立", kind = "Post", markdown = "x", status = "Draft" });
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        }
    }

    [PostgresFact]
    public async Task Content_lifecycle_round_trips_over_the_admin_api()
    {
        using var actor = await App.AdministratorAsync();
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var admin = await CreateAdminClientAsync(api, await App.AdminSecurityStampAsync());
        using var publicClient = api.CreateClient();

        using var created = await admin.PostAsJsonAsync("/api/v1/admin/contents",
            new { title = "管理文章", kind = "Post", markdown = "本文", status = "Draft" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        var id = (Guid)draft["id"]!;
        Assert.Equal("Draft", (string)draft["status"]!);

        using var published = await admin.PutAsJsonAsync($"/api/v1/admin/contents/{id}",
            new { title = "管理文章", slug = (string)draft["slug"]!, kind = "Post", markdown = "本文", status = "Published", version = (string)draft["version"]! });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var publishState = JsonNode.Parse(await published.Content.ReadAsStringAsync())!;
        Assert.Equal("Published", (string)publishState["status"]!);
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync("/api/v1/posts/管理文章")).StatusCode);

        using var withdrawn = await admin.PutAsJsonAsync($"/api/v1/admin/contents/{id}",
            new { title = "管理文章", slug = (string)draft["slug"]!, kind = "Post", markdown = "本文", status = "Draft", version = (string)publishState["version"]! });
        var withdrawnState = JsonNode.Parse(await withdrawn.Content.ReadAsStringAsync())!;
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/api/v1/posts/管理文章")).StatusCode);

        using var trashed = await admin.DeleteAsync($"/api/v1/admin/contents/{id}?version={withdrawnState["version"]!}");
        Assert.True(trashed.StatusCode == HttpStatusCode.NoContent,
            $"移入回收桶失敗：{(int)trashed.StatusCode} | {string.Join(" || ", api.ConsoleErrors)}");
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/api/v1/posts/管理文章")).StatusCode);

        using var trashedEdit = await admin.GetAsync($"/api/v1/admin/contents/{id}");
        var trashState = JsonNode.Parse(await trashedEdit.Content.ReadAsStringAsync())!;
        Assert.Equal("Trashed", (string)trashState["status"]!);

        using var restored = await admin.PostAsync($"/api/v1/admin/contents/{id}/restore?version={trashState["version"]!}", null);
        Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        using var afterRestore = await admin.GetAsync($"/api/v1/admin/contents/{id}");
        Assert.Equal("Draft", (string)JsonNode.Parse(await afterRestore.Content.ReadAsStringAsync())!["status"]!);
    }

    [PostgresFact]
    public async Task Stale_version_is_rejected_without_overwriting()
    {
        using var actor = await App.AdministratorAsync();
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var admin = await CreateAdminClientAsync(api, await App.AdminSecurityStampAsync());

        using var created = await admin.PostAsJsonAsync("/api/v1/admin/contents",
            new { title = "衝突文章", kind = "Post", markdown = "第一版", status = "Draft" });
        var first = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;

        using var second = await admin.PutAsJsonAsync($"/api/v1/admin/contents/{(Guid)first["id"]!}",
            new { title = "衝突文章", slug = (string)first["slug"]!, kind = "Post", markdown = "第二版", status = "Draft", version = (string)first["version"]! });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondState = JsonNode.Parse(await second.Content.ReadAsStringAsync())!;

        // 重放過期版本必須明確失敗，不得默默回退成「第一版」。
        using var stale = await admin.PutAsJsonAsync($"/api/v1/admin/contents/{(Guid)first["id"]!}",
            new { title = "衝突文章", slug = (string)first["slug"]!, kind = "Post", markdown = "過期版本", status = "Draft", version = (string)first["version"]! });
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        var error = JsonNode.Parse(await stale.Content.ReadAsStringAsync())!;
        Assert.Contains("另一個分頁", (string?)error["message"]);

        using var current = await admin.GetAsync($"/api/v1/admin/contents/{(Guid)first["id"]!}");
        var currentState = JsonNode.Parse(await current.Content.ReadAsStringAsync())!;
        Assert.Equal("第二版", (string)currentState["markdown"]!);
        Assert.Equal((string)secondState["version"]!, (string)currentState["version"]!);
    }

    [PostgresFact]
    public async Task Admin_traffic_has_its_own_generous_rate_budget()
    {
        using var actor = await App.AdministratorAsync();
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var admin = await CreateAdminClientAsync(api, await App.AdminSecurityStampAsync());
        for (var i = 0; i < 40; i++)
        {
            using var response = await admin.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
