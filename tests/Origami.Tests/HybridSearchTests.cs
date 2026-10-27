namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class HybridSearchTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Keyword_matching_finds_exact_terms_without_waiting_for_the_index()
    {
        using var admin = await App.AdministratorAsync();
        var exact = NewContent("部署筆記：ERR-4181 修正", status: ContentStatus.Published);
        exact.Slug = "exact-term";
        exact.Markdown = "升級後出現 ERR-4181，重啟服務即可恢復。";
        await admin.Content.SaveAsync(exact);
        var different = NewContent("寫作靈感整理術", status: ContentStatus.Published);
        different.Slug = "inspiration";
        different.Markdown = "如何收集日常靈感並轉成文章。";
        await admin.Content.SaveAsync(different);
        var hidden = NewContent("草稿裡的 ERR-4181");
        hidden.Slug = "hidden-term";
        hidden.Markdown = "草稿正文 ERR-4181 不應被找到。";
        await admin.Content.SaveAsync(hidden);

        var search = admin.Services.GetRequiredService<PublicSearchService>();
        // 尚未產生任何嵌入向量：關鍵字比對這一路必須獨力給出結果。
        var result = await search.SearchAsync("ERR-4181");
        var hit = Assert.Single(result.Hits);
        Assert.Equal("exact-term", hit.Slug);
        Assert.Contains("ERR-4181", hit.Snippet, StringComparison.Ordinal);
        Assert.Contains("關鍵字", hit.Source, StringComparison.Ordinal);

        // 標題比對對 ASCII 字母不分大小寫。
        var titled = NewContent("WordPress 遷移心得", status: ContentStatus.Published);
        titled.Slug = "wp-migration";
        await admin.Content.SaveAsync(titled);
        var caseInsensitive = await search.SearchAsync("wordpress");
        Assert.Contains(caseInsensitive.Hits, x => x.Slug == "wp-migration");
    }

    [PostgresFact]
    public async Task Keyword_and_semantic_results_merge_without_duplicates()
    {
        using var admin = await App.AdministratorAsync();
        var exact = NewContent("含關鍵字的文章", status: ContentStatus.Published);
        exact.Slug = "both-legs";
        exact.Markdown = "這篇包含特殊代號 ZQX-99 於正文。";
        await admin.Content.SaveAsync(exact);
        var semanticOnly = NewContent("概念相近但無關鍵字", status: ContentStatus.Published);
        semanticOnly.Slug = "semantic-only";
        semanticOnly.Markdown = "完全不同的主題內容，僅透過語意檢索。";
        await admin.Content.SaveAsync(semanticOnly);
        await App.Worker.ProcessPendingAsync();

        var search = admin.Services.GetRequiredService<PublicSearchService>();
        var result = await search.SearchAsync("ZQX-99");
        Assert.True(result.SemanticAvailable);
        var slugs = result.Hits.Select(x => x.Slug).ToList();
        Assert.Equal("both-legs", slugs.First());
        Assert.Single(slugs, "both-legs");
        Assert.Contains("semantic-only", slugs);
        Assert.Equal("關鍵字＋語意", result.Hits[0].Source);
        Assert.Contains("語意", result.Hits.Single(x => x.Slug == "semantic-only").Source, StringComparison.Ordinal);
        // 撤回後的下一次搜尋即不再出現關鍵字命中，與 404 規則一致。
        var withdraw = await admin.Content.GetEditAsync((await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Published)).Items.Single(x => x.Slug == "both-legs").Id);
        withdraw.Status = ContentStatus.Draft;
        await admin.Content.UpdateAsync(withdraw);
        var after = await search.SearchAsync("ZQX-99");
        Assert.DoesNotContain(after.Hits, x => x.Slug == "both-legs");
    }

    [PostgresFact]
    public async Task The_public_search_page_renders_keyword_hits_with_their_source()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("公開搜尋的目標文章", status: ContentStatus.Published);
        post.Slug = "search-target";
        post.Markdown = "正文中含有 TRX-7 這個代號。";
        await admin.Content.SaveAsync(post);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var response = await client.GetAsync("/search?q=TRX-7");
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"status={(int)response.StatusCode} body={responseText[..Math.Min(600, responseText.Length)]}");
        var html = WebUtility.HtmlDecode(responseText);
        Assert.Contains("公開搜尋的目標文章", html);
        Assert.Contains("關鍵字", html);
        Assert.DoesNotContain("沒有找到文章", html);
    }
}
