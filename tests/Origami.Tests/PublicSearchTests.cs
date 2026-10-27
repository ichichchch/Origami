namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PublicSearchTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Readers_can_submit_search_follow_results_and_recover_from_provider_errors_on_mobile()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("整理寫作靈感", status: ContentStatus.Published);
        post.Slug = "writing-ideas";
        post.Markdown = "先記下觀察，再把相近的想法分組，逐步整理成文章。";
        await admin.Content.SaveAsync(post);
        await App.Worker.ProcessPendingAsync();
        await using var provider = await LoopbackEmbeddingProvider.StartAsync();
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync(configurationOverrides: provider.Configuration);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge", Headless = true
        });
        await using var context = await browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString(),
            ViewportSize = new() { Width = 390, Height = 844 }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.GetByRole(AriaRole.Link, new() { Name = "搜尋文章", Exact = true }).ClickAsync();
        var query = page.GetByLabel("搜尋文字", new() { Exact = true });
        var submit = page.GetByRole(AriaRole.Button, new() { Name = "搜尋文章", Exact = true });
        await Expect(page.Locator("#search-help")).ToContainTextAsync("外部嵌入模型");
        await query.FillAsync(" ");
        await submit.ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).Query.Length > 0);
        await Expect(page.Locator(".empty-state")).ToContainTextAsync("你想找什麼");
        Assert.Equal(0, provider.Requests);
        await query.FillAsync("如何整理寫作靈感？");
        await submit.ClickAsync();
        await Expect(page.Locator(".search-results li")).ToHaveCountAsync(1);
        await Expect(page.Locator(".search-snippet")).ToContainTextAsync("先記下觀察");
        Assert.Equal(1, provider.Requests);
        var screenshots = Path.Combine(RunningSite.FindSolutionRoot(), ".artifacts", "screenshots");
        Directory.CreateDirectory(screenshots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, "public-search-mobile.png"), FullPage = true });
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        await page.Locator(".search-results").GetByRole(AriaRole.Link, new() { Name = post.Title, Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/posts/writing-ideas");
        await Expect(page.Locator("h1")).ToHaveTextAsync(post.Title);
        await page.GetByRole(AriaRole.Link, new() { Name = "搜尋文章", Exact = true }).ClickAsync();
        // An unbroken query must wrap in the result heading rather than widen the mobile page.
        await query.FillAsync(new string('W', Limits.SearchQueryMax));
        await submit.ClickAsync();
        await Expect(page.Locator(".search-results li")).ToHaveCountAsync(1);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        provider.Fail = true;
        await query.FillAsync("供應商失敗後仍可修改問題");
        await submit.ClickAsync();
        // A provider failure degrades to keyword-only results; the query and page stay usable.
        await Expect(page.Locator(".field-help").Filter(new() { HasText = "語意搜尋暫時無法使用" })).ToBeVisibleAsync();
        await Expect(query).ToHaveValueAsync("供應商失敗後仍可修改問題");
        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        var html = await page.ContentAsync();
        Assert.DoesNotContain(provider.Endpoint, html);
        Assert.DoesNotContain(LoopbackEmbeddingProvider.TestKey, html);
        provider.Fail = false;
        await query.FillAsync("重新搜尋");
        await submit.ClickAsync();
        await Expect(page.Locator(".search-results li")).ToHaveCountAsync(1);
        await Expect(page.Locator(".field-help").Filter(new() { HasText = "語意搜尋暫時無法使用" })).ToHaveCountAsync(0);
    }

    [PostgresFact]
    public async Task Static_get_form_guides_blank_queries_and_rejects_overlong_input_without_model_cost()
    {
        await using var provider = await LoopbackEmbeddingProvider.StartAsync();
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync(configurationOverrides: provider.Configuration);
        using var client = site.CreateClient();
        foreach (var path in new[] { "/search", "/search?q=", "/search?q=%20%09%20" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var document = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
            Assert.Contains("你想找什麼", document.Body!.TextContent);
            var form = document.QuerySelector("form[role=search]")!;
            Assert.Equal("get", form.GetAttribute("method"));
            Assert.Equal("/search", form.GetAttribute("action"));
            Assert.Equal("搜尋文字", form.QuerySelector("label[for=search-query]")!.TextContent);
            Assert.Equal("q", form.QuerySelector("input")!.GetAttribute("name"));
            Assert.Equal(Limits.SearchQueryMax.ToString(CultureInfo.InvariantCulture), form.QuerySelector("input")!.GetAttribute("maxlength"));
            Assert.DoesNotContain("\"type\":\"server\"", document.DocumentElement.OuterHtml);
        }
        using (var tooLong = await client.GetAsync("/search?q=" + new string('x', Limits.SearchQueryMax + 1)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Contains("500", await tooLong.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, provider.Requests);
        using var boundary = await client.GetAsync("/search?q=" + new string('x', Limits.SearchQueryMax));
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
        Assert.Contains("沒有找到文章", WebUtility.HtmlDecode(await boundary.Content.ReadAsStringAsync()));
        Assert.Equal(1, provider.Requests);
    }

    [PostgresFact]
    public async Task Results_encode_untrusted_text_and_never_expose_drafts_pages_withdrawn_or_deleted_posts()
    {
        App.Embeddings.VectorFor = _ => FakeEmbeddingGateway.Basis(0);
        using var admin = await App.AdministratorAsync();
        var post = NewContent("<script>alert(1)</script> & title", status: ContentStatus.Published);
        post.Slug = "safe-post";
        post = await admin.Content.SaveAsync(post);
        var second = await admin.Content.SaveAsync(NewContent("Second public post", status: ContentStatus.Published));
        var draft = await admin.Content.SaveAsync(NewContent("SECRET DRAFT"));
        await admin.Content.SaveAsync(NewContent("EXCLUDED PAGE", ContentKind.Page, ContentStatus.Published));
        await App.Worker.ProcessPendingAsync();
        const string snippet = "<img src=x onerror=alert(2)> & snippet";
        await using (var db = await App.CreateContextAsync())
        {
            (await db.ContentEmbeddings.SingleAsync(x => x.ContentId == post.Id)).ChunkText = snippet;
            db.ContentEmbeddings.Add(new ContentEmbedding
            {
                ContentId = draft.Id!.Value, ChunkIndex = 0, ChunkText = "SECRET VECTOR",
                Embedding = new Vector(FakeEmbeddingGateway.Basis(0))
            });
            await db.SaveChangesAsync();
        }
        await using var provider = await LoopbackEmbeddingProvider.StartAsync();
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync(configurationOverrides: provider.Configuration);
        using var client = site.CreateClient();
        const string query = "\"><img src=x onerror=alert(3)>";
        using (var response = await client.GetAsync("/search?q=" + Uri.EscapeDataString(query)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            var document = new HtmlParser().ParseDocument(html);
            Assert.Equal(query, document.QuerySelector("#search-query")!.GetAttribute("value"));
            Assert.Equal(post.Title, document.QuerySelector("a[href='/posts/safe-post']")!.TextContent);
            Assert.Contains(snippet, document.QuerySelector(".search-results")!.TextContent);
            Assert.Empty(document.QuerySelectorAll(".search-page img, .search-page script"));
            Assert.DoesNotContain("SECRET", html);
            // Published pages remain legitimate navigation links, but are not article search results.
            Assert.DoesNotContain("EXCLUDED PAGE", document.QuerySelector(".search-results")!.TextContent);
            Assert.Empty(document.QuerySelectorAll(".search-results a[href^='/pages/']"));
            Assert.Contains(HtmlEncoder.Default.Encode(snippet), html);
            using var detail = await client.GetAsync("/posts/safe-post");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        }
        post.Status = ContentStatus.Draft;
        await admin.Content.SaveAsync(post);
        await admin.Content.DeleteAsync(second.Id!.Value, second.Version, confirmed: true);
        using var after = await client.GetAsync("/search?q=post");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Contains("沒有找到文章", WebUtility.HtmlDecode(await after.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/safe-post")).StatusCode);
    }

    [PostgresFact]
    public async Task Missing_configuration_and_provider_failures_degrade_to_keyword_only_without_leaking_details()
    {
        // Without any embedding configuration the keyword leg still answers; nothing else is exposed.
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync(); // Inherited credentials are explicitly cleared by RunningSite.
        using (var client = site.CreateClient())
        using (var response = await client.GetAsync("/search?q=hello"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Contains("語意搜尋暫時無法使用", html);
            Assert.DoesNotContain("ApiKey", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Ai:Embedding", html);
            Assert.DoesNotContain("Exception", html);
        }
        await site.StopAsync();

        await using var provider = await LoopbackEmbeddingProvider.StartAsync();
        provider.Fail = true;
        await site.StartAsync(configurationOverrides: provider.Configuration);
        using var configuredClient = site.CreateClient();
        using var failure = await configuredClient.GetAsync("/search?q=hello");
        Assert.Equal(HttpStatusCode.OK, failure.StatusCode);
        Assert.Equal(1, provider.Requests);
        var failureHtml = WebUtility.HtmlDecode(await failure.Content.ReadAsStringAsync());
        Assert.Contains("語意搜尋暫時無法使用", failureHtml);
        Assert.DoesNotContain(provider.Endpoint, failureHtml);
        Assert.DoesNotContain(LoopbackEmbeddingProvider.TestKey, failureHtml);
        Assert.DoesNotContain("PRIVATE PROVIDER ERROR", failureHtml);
    }

    [PostgresFact]
    public async Task Search_is_limited_to_thirty_requests_per_ip_even_with_mixed_path_case()
    {
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        for (var i = 0; i < 30; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"192.0.2.{i + 1}");
            using var response = await client.GetAsync("/SeArCh?q=%20");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        // A spoofed X-Forwarded-For must not buy a fresh budget: the 31st search from the same source is rejected.
        using var search = await client.GetAsync("/search?q=%20");
        Assert.Equal(HttpStatusCode.TooManyRequests, search.StatusCode);
        Assert.Equal("Too many requests.", await search.Content.ReadAsStringAsync());
        // The MCP endpoint now lives on the Origami.Api host; the website no longer treats /mcp as machine traffic.
        using var mcp = await PingAsync(client);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, mcp.StatusCode);
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }

    private static async Task<HttpResponseMessage> PingAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/McP")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return await client.SendAsync(request);
    }
}

public sealed class SearchTestEnvironmentTests
{
    [Fact]
    public void Ai_credentials_and_aliases_are_cleared_before_case_insensitive_overrides()
    {
        // A case-sensitive dictionary also reproduces Linux's duplicate environment-key behavior on Windows.
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AI__EMBEDDING__APIKEY"] = "inherited-secret",
            ["Ai__Embedding__ApiKey"] = "inherited-secret",
            ["ai:assistant:apikey"] = "inherited-secret",
            ["ASPNETCORE_Ai__Assistant__ApiKey"] = "inherited-secret",
            ["DOTNET_AI__EMBEDDING__APIKEY"] = "inherited-secret",
            ["Ai__Embedding__Endpoint"] = "https://external.invalid/embeddings",
            ["unrelated"] = "keep"
        };
        RunningSite.ConfigureAiEnvironment(environment);
        Assert.DoesNotContain("inherited-secret", environment.Values);
        Assert.DoesNotContain("https://external.invalid/embeddings", environment.Values);
        Assert.Equal("", environment["Ai__Embedding__ApiKey"]);
        Assert.Equal("", environment["Ai__Assistant__ApiKey"]);
        Assert.Equal("keep", environment["unrelated"]);
        RunningSite.ConfigureAiEnvironment(environment, new Dictionary<string, string?>
        {
            ["ai__EMBEDDING__apikey"] = LoopbackEmbeddingProvider.TestKey,
            ["AI:Embedding:Endpoint"] = "http://127.0.0.1:12345/embeddings"
        });
        Assert.Single(environment.Keys, x => x.Replace("__", ":", StringComparison.Ordinal)
            .Equals("Ai:Embedding:ApiKey", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LoopbackEmbeddingProvider.TestKey, environment["ai__EMBEDDING__apikey"]);
        Assert.Equal("", environment["Ai__Assistant__ApiKey"]);
    }
}

/// <summary>Real loopback HTTP, deterministic vectors, no vendor SDK/server or external credentials.</summary>
internal sealed class LoopbackEmbeddingProvider(WebApplication app) : IAsyncDisposable
{
    internal const string TestKey = "isolated-fake-key-not-a-credential";
    private int requests;
    public int Requests => Volatile.Read(ref requests);
    public bool Fail { get; set; }
    public string? AssistantReply { get; set; }
    public string Endpoint => app.Urls.Single().TrimEnd('/') + "/embeddings";
    public IReadOnlyDictionary<string, string?> Configuration => new Dictionary<string, string?>
    {
        ["Ai__Embedding__Endpoint"] = Endpoint,
        ["Ai__Embedding__Model"] = "fake-embedding",
        ["Ai__Embedding__ApiKey"] = TestKey,
        ["Ai__Assistant__Endpoint"] = app.Urls.Single(),
        ["Ai__Assistant__Model"] = "fake-assistant",
        ["Ai__Assistant__ApiKey"] = ""
    };

    public static async Task<LoopbackEmbeddingProvider> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var provider = new LoopbackEmbeddingProvider(app);
        app.Use(async (context, next) =>
        {
            Interlocked.Increment(ref provider.requests);
            await next(context);
        });
        app.MapPost("/embeddings", async (HttpContext context) =>
        {
            if (provider.Fail)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsync("PRIVATE PROVIDER ERROR " + TestKey);
                return;
            }
            using var input = await JsonDocument.ParseAsync(context.Request.Body);
            var data = input.RootElement.GetProperty("input").EnumerateArray()
                .Select((_, index) => new { index, embedding = FakeEmbeddingGateway.Basis(0) }).ToArray();
            await context.Response.WriteAsJsonAsync(new { data });
        });
        app.MapPost("/chat/completions", async (HttpContext context) =>
        {
            if (provider.AssistantReply is null)
            {
                // A deterministic non-retryable provider failure for the browser's recovery flow.
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = new { message = "PRIVATE PROVIDER ERROR " + TestKey, type = "invalid_request_error" } });
                return;
            }
            await context.Response.WriteAsJsonAsync(new
            {
                id = "chatcmpl-isolated-test", @object = "chat.completion", created = 0, model = "fake-assistant",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = provider.AssistantReply }, finish_reason = "stop" } }
            });
        });
        try
        {
            await app.StartAsync();
            return provider;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
