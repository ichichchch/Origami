namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AiStatusTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Status_counts_published_articles_not_chunks_and_includes_retries_in_pending_total()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        await using (var db = await App.CreateContextAsync())
        {
            var ready = Article("ready");
            var pending = Article("pending");
            var retry = Article("retry");
            var laterRetry = Article("later-retry");
            var unindexed = Article("unindexed");
            var draft = Article("draft");
            draft.Status = ContentStatus.Draft;
            var page = Article("page");
            page.Kind = ContentKind.Page;
            db.Contents.AddRange(ready, pending, retry, laterRetry, unindexed, draft, page);
            db.ContentEmbeddings.AddRange(Chunk(ready), Chunk(ready, 1), Chunk(pending), Chunk(retry), Chunk(draft), Chunk(page));
            db.EmbeddingJobs.AddRange(
                Job(pending, 0, now.AddMinutes(-1)), Job(retry, 2, now),
                Job(laterRetry, 1, now.AddHours(1)), Job(page, 0, now.AddMinutes(-5)));
            await db.SaveChangesAsync();
        }
        using var admin = await App.AdministratorAsync();
        var service = admin.Services.GetRequiredService<AiStatusService>();
        var before = await SnapshotAsync();
        var status = await service.GetAsync();
        Assert.Equal(5, status.PublishedPosts);
        Assert.Equal(1, status.IndexedPosts);
        Assert.Equal(4, status.PendingJobs);
        Assert.Equal(2, status.RetryJobs);
        Assert.Equal(now, status.EarliestRetryAt);
        Assert.False(status.EmbeddingConfigured);
        Assert.False(status.AssistantConfigured);
        Assert.Equal(status, await service.GetAsync());
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(0, App.Embeddings.EmbeddedTexts);
        Assert.Empty(App.Chat.Calls);
    }

    [PostgresFact]
    public async Task Empty_status_is_zero_and_configuration_refresh_does_not_probe_models_or_return_secrets()
    {
        using var admin = await App.AdministratorAsync();
        var service = admin.Services.GetRequiredService<AiStatusService>();
        var empty = await service.GetAsync();
        Assert.Equal(new AiStatus(false, false, 0, 0, 0, 0, null), empty);
        SetOptions(CompleteOptions());
        var complete = await service.GetAsync();
        Assert.True(complete.EmbeddingConfigured);
        Assert.True(complete.AssistantConfigured);
        // 指定的 loopback 連接刻意沒有伺服器在聽；配置完整不代表連線正常。
        Assert.Equal(0, App.Embeddings.EmbeddedTexts);
        Assert.Empty(App.Chat.Calls);
        var serialized = JsonSerializer.Serialize(complete);
        Assert.DoesNotContain("127.0.0.1", serialized);
        Assert.DoesNotContain(LoopbackEmbeddingProvider.TestKey, serialized);
        SetOptions(new AiOptions());
        Assert.Equal(empty, await service.GetAsync());
    }

    [PostgresTheory]
    [InlineData("endpoint", "")]
    [InlineData("endpoint", "/relative/embeddings")]
    [InlineData("endpoint", "not a uri")]
    [InlineData("endpoint", "file:///private/model")]
    [InlineData("endpoint", "ftp://example.invalid/model")]
    [InlineData("endpoint", "https://user:password@example.invalid/model")]
    [InlineData("endpoint", "https://example.invalid/model#fragment")]
    [InlineData("model", " \t")]
    [InlineData("key", "")]
    [InlineData("key", "bad\r\nheader")]
    public async Task Missing_or_invalid_configuration_disables_only_the_affected_model(string field, string value)
    {
        using var admin = await App.AdministratorAsync();
        var service = admin.Services.GetRequiredService<AiStatusService>();
        var options = CompleteOptions();
        switch (field)
        {
            case "endpoint": options.Embedding.Endpoint = value; break;
            case "model": options.Embedding.Model = value; break;
            case "key": options.Embedding.ApiKey = value; break;
        }
        SetOptions(options);
        var embedding = await service.GetAsync();
        Assert.False(embedding.EmbeddingConfigured);
        Assert.True(embedding.AssistantConfigured);
        options = CompleteOptions();
        switch (field)
        {
            case "endpoint": options.Assistant.Endpoint = value; break;
            case "model": options.Assistant.Model = value; break;
            case "key": options.Assistant.ApiKey = value; break;
        }
        SetOptions(options);
        var assistant = await service.GetAsync();
        Assert.True(assistant.EmbeddingConfigured);
        Assert.False(assistant.AssistantConfigured);
        Assert.Equal(0, App.Embeddings.EmbeddedTexts);
        Assert.Empty(App.Chat.Calls);
    }

    [PostgresFact]
    public async Task Anonymous_and_revoked_administrators_cannot_read_status_or_even_inspect_configuration()
    {
        var monitor = new ThrowingOptionsMonitor();
        using var anonymous = App.Anonymous();
        var service = new AiStatusService(anonymous.Services.GetRequiredService<AdminAccess>(),
            App.ContextFactory, monitor);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync());
        Assert.Equal(0, monitor.Reads);
        using var stale = await App.AdministratorAsync();
        var protectedService = stale.Services.GetRequiredService<AiStatusService>();
        await protectedService.GetAsync();
        using (var revoker = App.Anonymous())
        {
            var user = await revoker.Users.Users.SingleAsync();
            Assert.True((await revoker.Users.UpdateSecurityStampAsync(user)).Succeeded);
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => protectedService.GetAsync());
    }

    [PostgresFact]
    public async Task Status_failures_are_generic_and_do_not_attach_sensitive_inner_exceptions()
    {
        using var admin = await App.AdministratorAsync();
        var service = new AiStatusService(admin.Services.GetRequiredService<AdminAccess>(),
            App.ContextFactory, new ThrowingOptionsMonitor());
        var error = await Assert.ThrowsAsync<CmsException>(() => service.GetAsync());
        Assert.Equal("目前無法讀取 AI 狀態，請稍後重試。", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(LoopbackEmbeddingProvider.TestKey, error.ToString());
        Assert.DoesNotContain("private.invalid", error.ToString());
    }

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Assistant_explains_read_only_scope_and_setup_and_refreshes_without_model_calls(
        bool embeddingConfigured, bool assistantConfigured)
    {
        var configured = embeddingConfigured && assistantConfigured;
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var provider = await LoopbackEmbeddingProvider.StartAsync();
        var configuration = provider.Configuration.ToDictionary(x => x.Key, x => x.Value);
        configuration["Ai__Embedding__ApiKey"] = embeddingConfigured ? LoopbackEmbeddingProvider.TestKey : "";
        configuration["Ai__Assistant__ApiKey"] = assistantConfigured ? LoopbackEmbeddingProvider.TestKey : "";
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync(configurationOverrides: configuration);
        using (var client = site.CreateClient())
        using (var response = await client.GetAsync("/admin/assistant"))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge", Headless = true
        });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/admin/assistant");
        await page.GetByLabel("帳號", new() { Exact = true }).FillAsync("test-admin");
        await page.GetByLabel("密碼", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "登入", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");
        await page.GotoAsync("/admin/assistant");
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "刷新狀態", Exact = true });
        await Expect(refresh).ToBeEnabledAsync();
        await Expect(page.Locator(".page-heading")).ToContainTextAsync("不是 AI 寫作工具");
        await Expect(page.Locator(".ai-status")).ToContainTextAsync("配置完整不代表連線正常");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "建立文章", Exact = true })).ToHaveAttributeAsync("href", "/admin/posts/new");
        foreach (var model in new[] { "Embedding", "Assistant" })
            foreach (var field in new[] { "Endpoint", "Model", "ApiKey" })
                await Expect(page.Locator(".ai-setup")).ToContainTextAsync($"Ai__{model}__{field}");
        var question = page.GetByLabel("你的問題", new() { Exact = true });
        if (configured)
        {
            await Expect(question).ToBeEnabledAsync();
            await question.FillAsync("保留這個尚未送出的問題");
        }
        else
        {
            await Expect(question).ToBeDisabledAsync();
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "送出問題", Exact = true })).ToBeDisabledAsync();
        }
        // 以單一交易植入一篇就緒文章，且不留下供網站背景工作執行的到期工作。
        await using (var db = await App.CreateContextAsync())
        {
            var article = Article("new-ready-article");
            db.Contents.Add(article);
            db.ContentEmbeddings.Add(Chunk(article));
            await db.SaveChangesAsync();
        }
        await refresh.ClickAsync();
        await Expect(page.Locator(".ai-status-grid div").Filter(new() { Has = page.Locator("dt", new() { HasTextString = "已發布文章" }) }).Locator("dd")).ToHaveTextAsync("1");
        await Expect(page.Locator(".ai-status-grid div").Filter(new() { Has = page.Locator("dt", new() { HasTextString = "已索引且無待處理工作的文章" }) }).Locator("dd")).ToHaveTextAsync("1");
        if (configured) await Expect(question).ToHaveValueAsync("保留這個尚未送出的問題");
        Assert.Equal(0, provider.Requests);
        var html = await page.ContentAsync();
        Assert.DoesNotContain(provider.Endpoint, html);
        Assert.DoesNotContain(LoopbackEmbeddingProvider.TestKey, html);
        await page.SetViewportSizeAsync(390, 844);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        if (!embeddingConfigured && !assistantConfigured)
        {
            var directory = Path.Combine(RunningSite.FindSolutionRoot(), ".artifacts", "screenshots");
            Directory.CreateDirectory(directory);
            // 不先捲回頂端的話，全頁截圖會把上一個捲動位置、視窗外的固定連結一併拍進去。
            await page.EvaluateAsync("window.scrollTo({ top: 0, behavior: 'instant' })");
            Assert.True(await page.Locator(".skip-link").EvaluateAsync<bool>("element => element.getBoundingClientRect().bottom < 0"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "ai-status-mobile.png"), FullPage = true });
        }
        if (configured)
        {
            // 實際演練真實的 Chat Completions 用戶端與 UI，但不送任何資料給供應商。
            var before = await SnapshotAsync();
            provider.AssistantReply = "**已找到文章**，請閱讀 [測試文章](/posts/new-ready-article)。<script>alert(1)</script>";
            var send = page.GetByRole(AriaRole.Button, new() { Name = "送出問題", Exact = true });
            await send.ClickAsync();
            await Expect(page.Locator(".assistant-user")).ToContainTextAsync("保留這個尚未送出的問題");
            await Expect(page.Locator(".assistant-bot strong")).ToHaveTextAsync("已找到文章");
            await Expect(page.Locator(".assistant-bot a")).ToHaveAttributeAsync("href", "/posts/new-ready-article");
            await Expect(page.Locator(".assistant-bot script")).ToHaveCountAsync(0);
            await Expect(question).ToHaveValueAsync("");
            Assert.Equal(1, provider.Requests);
            provider.AssistantReply = null;
            await question.FillAsync("供應商失敗時保留問題");
            await send.ClickAsync();
            await Expect(page.Locator(".notice-error")).ToContainTextAsync("助理暫時無法回覆");
            await Expect(question).ToHaveValueAsync("供應商失敗時保留問題");
            await Expect(page.Locator(".assistant-turn")).ToHaveCountAsync(2);
            Assert.DoesNotContain("PRIVATE PROVIDER ERROR", await page.ContentAsync());
            Assert.DoesNotContain(LoopbackEmbeddingProvider.TestKey, await page.ContentAsync());
            provider.AssistantReply = "重新回覆成功。";
            await send.ClickAsync();
            await Expect(page.Locator(".assistant-turn")).ToHaveCountAsync(4);
            await Expect(page.Locator(".notice-error")).ToHaveCountAsync(0);
            await page.GetByRole(AriaRole.Button, new() { Name = "清空對話", Exact = true }).ClickAsync();
            await Expect(page.Locator(".assistant-turn")).ToHaveCountAsync(0);
            Assert.Equal(3, provider.Requests);
            Assert.Equal(before, await SnapshotAsync());
            using var revoker = App.Anonymous();
            Assert.True((await revoker.Users.UpdateSecurityStampAsync(await revoker.Users.Users.SingleAsync())).Succeeded);
            await refresh.ClickAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "重新登入", Exact = true })).ToBeVisibleAsync();
            await Expect(question).ToBeDisabledAsync();
            Assert.Equal(3, provider.Requests);
        }
    }

    private void SetOptions(AiOptions options)
    {
        var cache = App.Services.GetRequiredService<IOptionsMonitorCache<AiOptions>>();
        cache.TryRemove(Options.DefaultName);
        Assert.True(cache.TryAdd(Options.DefaultName, options));
    }

    private static AiOptions CompleteOptions() => new()
    {
        Embedding = new() { Endpoint = "http://127.0.0.1:1/embeddings", Model = "fake-embedding", ApiKey = LoopbackEmbeddingProvider.TestKey },
        Assistant = new() { Endpoint = "http://127.0.0.1:1/v1", Model = "fake-assistant", ApiKey = LoopbackEmbeddingProvider.TestKey }
    };

    private static Content Article(string slug) => new()
    {
        Title = slug, Slug = slug, Markdown = "Indexable content", Kind = ContentKind.Post,
        Status = ContentStatus.Published, CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow, PublishedAt = DateTimeOffset.UtcNow
    };

    private static ContentEmbedding Chunk(Content content, int index = 0) => new()
    {
        ContentId = content.Id, ChunkIndex = index, ChunkText = content.Markdown,
        Embedding = new Vector(FakeEmbeddingGateway.Basis(0))
    };

    private static EmbeddingJob Job(Content content, int attempts, DateTimeOffset due) => new()
    {
        ContentId = content.Id, Attempts = attempts, NextAttemptAt = due, EnqueuedAt = due.AddHours(-1)
    };

    private async Task<string> SnapshotAsync()
    {
        await using var db = await App.CreateContextAsync();
        return JsonSerializer.Serialize(new
        {
            Contents = await db.Contents.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Embeddings = await db.ContentEmbeddings.AsNoTracking().OrderBy(x => x.ContentId).ThenBy(x => x.ChunkIndex).ToListAsync(),
            Jobs = await db.EmbeddingJobs.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
        });
    }

    private sealed class ThrowingOptionsMonitor : IOptionsMonitor<AiOptions>
    {
        public int Reads { get; private set; }
        public AiOptions CurrentValue
        {
            get
            {
                Reads++;
                throw new InvalidOperationException("https://private.invalid " + LoopbackEmbeddingProvider.TestKey);
            }
        }
        public AiOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AiOptions, string?> listener) => null;
    }
}
