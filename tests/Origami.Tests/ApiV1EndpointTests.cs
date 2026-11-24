namespace Origami.Tests;

/// <summary>以真實 HTTPS 對隔離的 Api 主機驅動唯讀 JSON API v1。</summary>
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
        // 頁面絕不出現在文章列表，與網站行為一致。
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
        // 隔離測試中 AI 端點保持停用：關鍵字比對仍可服務，語意則降級。
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
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/search?q=x&month=2")).StatusCode);
        var statuses = new List<int>();
        for (var i = 0; i < 40 && !statuses.Contains(429); i++)
            statuses.Add((int)(await client.GetAsync("/api/v1/posts")).StatusCode);
        Assert.True(statuses.Contains(429), "預期 /api 在 30 次後限流，實際狀態碼：" + string.Join(",", statuses));
    }

    [PostgresFact]
    public async Task Public_json_compresses_and_keeps_security_and_no_store_headers()
    {
        using var actor = await App.AdministratorAsync();
        var input = NewContent("API 壓縮文章", status: ContentStatus.Published);
        input.Markdown = new string('內', 20_000);
        var saved = await actor.Content.SaveAsync(input);

        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        using var response = await client.GetAsync("/api/v1/posts/" + saved.Slug);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Content.Headers.ContentEncoding, value => value == "gzip");
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains(response.Headers.GetValues("Content-Security-Policy"),
            policy => policy.Contains("frame-ancestors 'none'", StringComparison.Ordinal));

        await using var compressed = await response.Content.ReadAsStreamAsync();
        await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var body = JsonNode.Parse(await reader.ReadToEndAsync())!;
        Assert.Equal(input.Markdown, (string?)body["markdown"]);
    }

    private static async Task AssertOkWithMarkdownAsync(HttpClient client, string url, string markdown)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(markdown, (string?)body["markdown"]);
    }
}
