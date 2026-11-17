namespace Origami.Tests;

/// <summary>
/// WordPress 舊網址相容層與閱讀體驗的端到端驗證（RunningSite 完整 BFF 鏈路）：
/// /archives/{id} 301、分類／作者／分頁改寫、未登記舊編號 404、
/// 詳情頁服務端高亮與燈箱標記、ETag 條件請求 304。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WpLegacyRouteTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Legacy_wordpress_urls_redirect_and_detail_serves_reading_experience()
    {
        using var actor = await App.AdministratorAsync();
        var saved = await actor.Content.SaveAsync(new ContentEdit
        {
            Title = "雙 Token 筆記",
            Slug = "token-notes",
            Kind = ContentKind.Post,
            Status = ContentStatus.Published,
            AllowComments = false, // 含評論表單的頁面每次渲染的防偽權杖不同，ETag 天然不穩定；ETag 驗證用無表單內容。
            Markdown = "前文\n\n```csharp\nvar x = 1; // hi\n```\n\n![圖](https://example.com/i.png)"
        });
        var contentId = saved.Id ?? throw new InvalidOperationException("保存後應有內容識別。");
        await using var db = await App.CreateContextAsync();
        db.SlugRedirects.Add(new ContentSlugRedirect
        {
            SiteId = db.CurrentSiteId, OldSlug = "wp-999", ContentId = contentId, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        using var legacy = await client.GetAsync(new Uri(site.Address, "/archives/999"));
        Assert.Equal(HttpStatusCode.MovedPermanently, legacy.StatusCode);
        Assert.Equal("/posts/token-notes", legacy.Headers.Location?.ToString());

        using var category = await client.GetAsync(new Uri(site.Address, "/archives/category/dotnet"));
        Assert.Equal(HttpStatusCode.MovedPermanently, category.StatusCode);
        Assert.Equal("/?category=dotnet", category.Headers.Location?.ToString());

        using var author = await client.GetAsync(new Uri(site.Address, "/archives/author/ichi"));
        Assert.Equal(HttpStatusCode.MovedPermanently, author.StatusCode);
        Assert.Equal("/author/ichi", author.Headers.Location?.ToString());

        using var paged = await client.GetAsync(new Uri(site.Address, "/page/2"));
        Assert.Equal(HttpStatusCode.MovedPermanently, paged.StatusCode);
        Assert.Equal("/?page=2", paged.Headers.Location?.ToString());

        // 未登記的舊編號不猜測目標：404，不回退到任何文章。
        using var missing = await client.GetAsync(new Uri(site.Address, "/archives/12345"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var detail = await client.GetAsync(new Uri(site.Address, "/posts/token-notes"));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var html = await detail.Content.ReadAsStringAsync();
        Assert.Contains("code-block", html);
        Assert.Contains("img-lightbox", html);

        // 詳情頁 ETag 條件請求：以最靜態的獨立頁面驗證（無評論表單／相關文章區塊）；
        // 若主體含每次請求變動的內容，ETag 自然失配回 200，屬正確降級。
        var page = await actor.Content.SaveAsync(new ContentEdit
        {
            Title = "關於本站", Slug = "about-notes", Kind = ContentKind.Page,
            Status = ContentStatus.Published, Markdown = "固定內容"
        });
        string? etag = null;
        var stable = false;
        for (var attempt = 0; attempt < 6 && !stable; attempt++)
        {
            using var warm = await client.GetAsync(new Uri(site.Address, "/pages/about-notes"));
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
            var current = warm.Headers.ETag?.Tag;
            Assert.False(string.IsNullOrEmpty(current), "詳情頁應帶 ETag 標頭");
            if (current == etag) stable = true;
            else { etag = current; await Task.Delay(400); }
        }
        Assert.True(stable, "獨立頁面的 ETag 應保持穩定（內容未變時不受逐請求狀態註解影響）");
        using var revalidate = new HttpRequestMessage(HttpMethod.Get, new Uri(site.Address, "/pages/about-notes"));
        revalidate.Headers.IfNoneMatch.ParseAdd(etag!);
        using var conditional = await client.SendAsync(revalidate);
        Assert.Equal(HttpStatusCode.NotModified, conditional.StatusCode);
    }

    [PostgresFact]
    public async Task Indexnow_key_file_serves_only_the_configured_key()
    {
        using var actor = await App.AdministratorAsync();
        var settings = actor.Services.GetRequiredService<SettingsService>();
        var current = await settings.GetAsync();
        current.IndexNowEnabled = true;
        current.IndexNowKey = "a1b2c3d4e5f60718";
        await settings.SaveAsync(current);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        using var keyFile = await client.GetAsync(new Uri(site.Address, "/a1b2c3d4e5f60718.txt"));
        Assert.Equal(HttpStatusCode.OK, keyFile.StatusCode);
        Assert.Equal("a1b2c3d4e5f60718", await keyFile.Content.ReadAsStringAsync());

        using var wrong = await client.GetAsync(new Uri(site.Address, "/deadbeefdeadbeef.txt"));
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
    }
}
