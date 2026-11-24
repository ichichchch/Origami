namespace Origami.Tests;

/// <summary>分類與標籤的獨立網址：只列已發布文章、正式網址與分頁、不存在的詞彙回 404。</summary>
[Collection(PostgresCollection.Name)]
public sealed class TermArchiveTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Category_and_tag_archives_serve_only_published_posts_with_canonical_urls()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "旅遊", Description = "旅行與風景" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "海邊" });
        var published = NewContent("海邊遊記", status: ContentStatus.Published);
        published.CategoryIds = [category.Id!.Value];
        published.TagIds = [tag.Id!.Value];
        var post = await admin.Content.SaveAsync(published);
        var draft = NewContent("未發布遊記");
        draft.CategoryIds = [category.Id.Value];
        draft.TagIds = [tag.Id.Value];
        await admin.Content.SaveAsync(draft);
        await admin.Content.SaveAsync(NewContent("無關文章", status: ContentStatus.Published));

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var origin = site.Address.ToString().TrimEnd('/');

        using var categoryResponse = await client.GetAsync("/category/" + Uri.EscapeDataString(category.Slug));
        var categoryHtml = WebUtility.HtmlDecode(await categoryResponse.Content.ReadAsStringAsync());
        Assert.True(categoryResponse.StatusCode == HttpStatusCode.OK && categoryHtml.Contains("海邊遊記", StringComparison.Ordinal),
            "HTTP " + (int)categoryResponse.StatusCode + "；slug=" + category.Slug +
            "；頁面前段：" + categoryHtml[..Math.Min(1200, categoryHtml.Length)] +
            "；Web 錯誤：" + string.Join(" | ", site.ErrorLines) +
            "；站點解析：" + string.Join(" | ", site.SiteResolutionLines) +
            "；Api 錯誤：" + string.Join(" | ", site.ApiErrorLines));
        Assert.DoesNotContain("未發布遊記", categoryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("無關文章", categoryHtml, StringComparison.Ordinal);
        Assert.Contains("旅行與風景", categoryHtml, StringComparison.Ordinal);
        Assert.Contains("rel=\"canonical\" href=\"" + origin + "/category/" + Uri.EscapeDataString(category.Slug) + "\"", categoryHtml);
        Assert.Contains("href=\"/category/" + Uri.EscapeDataString(category.Slug) + "\"", categoryHtml);
        Assert.Contains("共 1 篇", categoryHtml, StringComparison.Ordinal);

        using var tagResponse = await client.GetAsync("/tag/" + Uri.EscapeDataString(tag.Slug));
        var tagHtml = WebUtility.HtmlDecode(await tagResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, tagResponse.StatusCode);
        Assert.Contains("海邊遊記", tagHtml, StringComparison.Ordinal);
        Assert.Contains("rel=\"canonical\" href=\"" + origin + "/tag/" + Uri.EscapeDataString(tag.Slug) + "\"", tagHtml);

        // 查詢字串形式仍可用，且單一詞彙篩選的 canonical 指向獨立網址。
        using var queryResponse = await client.GetAsync("/?category=" + Uri.EscapeDataString(category.Slug));
        var queryHtml = WebUtility.HtmlDecode(await queryResponse.Content.ReadAsStringAsync());
        Assert.True(queryResponse.StatusCode == HttpStatusCode.OK && queryHtml.Contains("海邊遊記", StringComparison.Ordinal),
            "HTTP " + (int)queryResponse.StatusCode + "；頁面前段：" + queryHtml[..Math.Min(1200, queryHtml.Length)] +
            "；Web 錯誤：" + string.Join(" | ", site.ErrorLines) +
            "；Api 錯誤：" + string.Join(" | ", site.ApiErrorLines));
        Assert.Contains("rel=\"canonical\" href=\"" + origin + "/category/" + Uri.EscapeDataString(category.Slug) + "\"", queryHtml);

        // 文章頁與網站地圖都使用獨立網址。
        using var detailResponse = await client.GetAsync("/posts/" + post.Slug);
        var detailHtml = await detailResponse.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/category/" + Uri.EscapeDataString(category.Slug) + "\"", detailHtml);
        Assert.Contains("href=\"/tag/" + Uri.EscapeDataString(tag.Slug) + "\"", detailHtml);
        using var sitemapResponse = await client.GetAsync("/sitemap.xml");
        var sitemap = await sitemapResponse.Content.ReadAsStringAsync();
        Assert.Contains("/category/" + Uri.EscapeDataString(category.Slug) + "</loc>", sitemap, StringComparison.Ordinal);
        Assert.Contains("/tag/" + Uri.EscapeDataString(tag.Slug) + "</loc>", sitemap, StringComparison.Ordinal);

        using var unknown = await client.GetAsync("/category/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var beyond = await client.GetAsync("/category/" + Uri.EscapeDataString(category.Slug) + "?page=2");
        Assert.Equal(HttpStatusCode.NotFound, beyond.StatusCode);
    }
}
