namespace Origami.Tests;

/// <summary>訂閱補強（Atom、分類／標籤 feed）與 oEmbed provider 的 HTTP 測試。</summary>
[Collection(PostgresCollection.Name)]
public sealed class SyndicationEmbedTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Feeds_support_taxonomy_filters_and_atom_format()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "旅遊" });
        var categoryId = category.Id!.Value;
        var categorized = NewContent("分類文章", status: ContentStatus.Published);
        categorized.CategoryIds = [categoryId];
        await admin.Content.SaveAsync(categorized);
        await admin.Content.SaveAsync(NewContent("未分類文章", status: ContentStatus.Published));
        var hidden = NewContent("分類草稿", status: ContentStatus.Draft);
        hidden.CategoryIds = [categoryId];
        await admin.Content.SaveAsync(hidden);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        using var all = await client.GetAsync("/feed");
        var allXml = await all.Content.ReadAsStringAsync();
        Assert.Contains("分類文章", allXml, StringComparison.Ordinal);
        Assert.Contains("未分類文章", allXml, StringComparison.Ordinal);
        Assert.DoesNotContain("分類草稿", allXml, StringComparison.Ordinal);

        using var filtered = await client.GetAsync("/feed?category=" + Uri.EscapeDataString(category.Slug));
        var filteredXml = await filtered.Content.ReadAsStringAsync();
        Assert.Contains("旅遊", filteredXml, StringComparison.Ordinal);
        Assert.Contains("分類文章", filteredXml, StringComparison.Ordinal);
        Assert.DoesNotContain("未分類文章", filteredXml, StringComparison.Ordinal);
        Assert.DoesNotContain("分類草稿", filteredXml, StringComparison.Ordinal);

        using var unknown = await client.GetAsync("/feed?category=not-exist");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var atom = await client.GetAsync("/feed/atom");
        var atomXml = await atom.Content.ReadAsStringAsync();
        Assert.Contains("http://www.w3.org/2005/Atom", atomXml, StringComparison.Ordinal);
        Assert.Contains("分類文章", atomXml, StringComparison.Ordinal);
        Assert.Contains("<entry>", atomXml, StringComparison.Ordinal);
        Assert.Equal("application/atom+xml", atom.Content.Headers.ContentType?.MediaType);

        using var atomFiltered = await client.GetAsync("/feed/atom?category=" + Uri.EscapeDataString(category.Slug));
        var atomFilteredXml = await atomFiltered.Content.ReadAsStringAsync();
        Assert.Contains("分類文章", atomFilteredXml, StringComparison.Ordinal);
        Assert.DoesNotContain("未分類文章", atomFilteredXml, StringComparison.Ordinal);

        // 篩選列表頁提供對應的訂閱連結。
        using var list = await client.GetAsync("/?category=" + Uri.EscapeDataString(category.Slug));
        var listHtml = await list.Content.ReadAsStringAsync();
        Assert.Contains("feed?category=", listHtml, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Oembed_provider_serves_published_urls_only()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("可嵌入文章", status: ContentStatus.Published));
        var locked = NewContent("受保護文章", status: ContentStatus.Published);
        locked.IsProtected = true;
        locked.NewProtectPassword = "secret-pass-123";
        await admin.Content.SaveAsync(locked);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var baseAddress = site.Address.ToString().TrimEnd('/');

        using var absolute = await client.GetAsync("/oembed?url=" + Uri.EscapeDataString(baseAddress + "/posts/可嵌入文章") + "&format=json");
        Assert.Equal(HttpStatusCode.OK, absolute.StatusCode);
        var payload = JsonNode.Parse(await absolute.Content.ReadAsStringAsync())!;
        Assert.Equal("rich", (string)payload["type"]!);
        Assert.Equal("可嵌入文章", (string)payload["title"]!);
        Assert.Contains("/posts/", (string)payload["html"]!, StringComparison.Ordinal);

        // 相對路徑與絕對網址同等接受。
        using var relative = await client.GetAsync("/oembed?url=" + Uri.EscapeDataString("/posts/可嵌入文章"));
        Assert.Equal(HttpStatusCode.OK, relative.StatusCode);

        // 受保護、不存在與不支援的格式都有明確結果。
        using var protectedPost = await client.GetAsync("/oembed?url=" + Uri.EscapeDataString("/posts/受保護文章"));
        Assert.Equal(HttpStatusCode.NotFound, protectedPost.StatusCode);
        using var garbage = await client.GetAsync("/oembed?url=/admin/posts");
        Assert.Equal(HttpStatusCode.NotFound, garbage.StatusCode);
        using var xml = await client.GetAsync("/oembed?url=" + Uri.EscapeDataString("/posts/可嵌入文章") + "&format=xml");
        Assert.Equal(HttpStatusCode.NotImplemented, xml.StatusCode);

        // 詳情頁 head 提供探索連結。
        using var detail = await client.GetAsync("/posts/可嵌入文章");
        var html = WebUtility.HtmlDecode(await detail.Content.ReadAsStringAsync());
        Assert.Contains("application/json+oembed", html, StringComparison.Ordinal);
    }
}
