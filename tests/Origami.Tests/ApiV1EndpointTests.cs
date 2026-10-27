namespace Origami.Tests;

/// <summary>Drives the read-only JSON API v1 over real HTTPS against the isolated Api host.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ApiV1EndpointTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Posts_list_returns_only_published_posts_with_paging()
    {
        using var actor = await App.AdministratorAsync();
        for (var i = 1; i <= 3; i++)
            await actor.Content.SaveAsync(new ContentEdit { Title = $"列表文章{i}", Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = $"內容{i}" });
        await actor.Content.SaveAsync(new ContentEdit { Title = "列表草稿", Kind = ContentKind.Post, Status = ContentStatus.Draft, Markdown = "草稿" });
        // Pages never appear in the posts list, mirroring the website.
        await actor.Content.SaveAsync(new ContentEdit { Title = "列表頁面", Kind = ContentKind.Page, Status = ContentStatus.Published, Markdown = "頁面" });

        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();
        using var response = await client.GetAsync("/api/v1/posts?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(3, (int)body["total"]!);
        Assert.Equal(2, body["items"]!.AsArray().Count);
        Assert.All(body["items"]!.AsArray(), item => Assert.Equal("Post", item!["kind"]!.GetValue<string>()));
        Assert.DoesNotContain(body["items"]!.AsArray(), item => item!["title"]!.GetValue<string>().Contains("草稿"));

        using var health = await client.GetAsync("/health");
        Assert.True(health.IsSuccessStatusCode, "/health 應為成功狀態碼。");
    }

    [PostgresFact]
    public async Task Detail_endpoints_hide_unpublished_protected_and_private_content()
    {
        using var actor = await App.AdministratorAsync();
        await actor.Content.SaveAsync(new ContentEdit { Title = "詳情文章", Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = "公開本文" });
        await actor.Content.SaveAsync(new ContentEdit { Title = "草稿文章", Kind = ContentKind.Post, Status = ContentStatus.Draft, Markdown = "草稿" });
        await actor.Content.SaveAsync(new ContentEdit { Title = "保護文章", Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = "秘密", IsProtected = true, NewProtectPassword = "Protect-1aB!" });
        await actor.Content.SaveAsync(new ContentEdit { Title = "私密文章", Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = "私密", IsPrivate = true });
        await actor.Content.SaveAsync(new ContentEdit { Title = "公開頁面", Kind = ContentKind.Page, Status = ContentStatus.Published, Markdown = "頁面本文" });

        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();
        await AssertOkWithMarkdownAsync(client, "/api/v1/posts/詳情文章", "公開本文");
        await AssertOkWithMarkdownAsync(client, "/api/v1/pages/公開頁面", "頁面本文");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/posts/草稿文章")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/posts/保護文章")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/posts/私密文章")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/pages/不存在頁面")).StatusCode);
    }

    [PostgresFact]
    public async Task Search_returns_published_hits_and_degrades_without_an_embedding_key()
    {
        using var actor = await App.AdministratorAsync();
        await actor.Content.SaveAsync(new ContentEdit { Title = "搜尋文章", Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = "獨特關鍵字XYZ" });
        await actor.Content.SaveAsync(new ContentEdit { Title = "搜尋草稿", Kind = ContentKind.Post, Status = ContentStatus.Draft, Markdown = "獨特關鍵字XYZ" });

        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();
        using var response = await client.GetAsync("/api/v1/search?q=" + Uri.EscapeDataString("獨特關鍵字XYZ"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        // AI endpoints stay disabled in isolated tests: keyword matching still serves, semantics degrade.
        Assert.False((bool)body["semanticAvailable"]!);
        var hits = body["hits"]!.AsArray();
        Assert.Contains(hits, hit => hit!["title"]!.GetValue<string>() == "搜尋文章");
        Assert.DoesNotContain(hits, hit => hit!["title"]!.GetValue<string>() == "搜尋草稿");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/search?q=")).StatusCode);
    }

    [PostgresFact]
    public async Task Api_rejects_invalid_parameters_and_rate_limits_bursts()
    {
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/posts?pageSize=999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/posts?month=13")).StatusCode);
        var statuses = new List<int>();
        for (var i = 0; i < 40 && !statuses.Contains(429); i++)
            statuses.Add((int)(await client.GetAsync("/api/v1/posts")).StatusCode);
        Assert.True(statuses.Contains(429), "預期 /api 在 30 次後限流，實際狀態碼：" + string.Join(",", statuses));
    }

    private static async Task AssertOkWithMarkdownAsync(HttpClient client, string url, string markdown)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(markdown, (string?)body["markdown"]);
    }
}
