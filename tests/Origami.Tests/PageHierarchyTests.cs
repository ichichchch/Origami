namespace Origami.Tests;

/// <summary>頁面層級網址（/pages/parent/child/）與修訂差異比對。</summary>
[Collection(PostgresCollection.Name)]
public sealed class PageHierarchyTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [Fact]
    public void Text_diff_marks_added_removed_and_unchanged_lines()
    {
        var diff = TextDiff.Compare("第一行\n第二行\n第三行", "第一行\n修改行\n第三行\n第四行");
        Assert.NotNull(diff);
        Assert.Contains(diff, l => l.Kind == TextDiff.DiffKind.Unchanged && l.Text == "第一行");
        Assert.Contains(diff, l => l.Kind == TextDiff.DiffKind.Removed && l.Text == "第二行");
        Assert.Contains(diff, l => l.Kind == TextDiff.DiffKind.Added && l.Text == "修改行");
        Assert.Contains(diff, l => l.Kind == TextDiff.DiffKind.Added && l.Text == "第四行");
        // 行數超過上限時退回 null，由呼叫端改用全文對照。
        var huge = string.Join("\n", Enumerable.Range(0, 10).Select(_ => "行"));
        Assert.Null(TextDiff.Compare(huge, huge, maxLines: 5));
    }

    [PostgresFact]
    public async Task Page_path_resolution_returns_canonical_chain_and_falls_back_flat()
    {
        using var admin = await App.AdministratorAsync();
        var parent = NewContent("母頁面", kind: ContentKind.Page, status: ContentStatus.Published);
        await admin.Content.SaveAsync(parent);
        var child = NewContent("子頁面", kind: ContentKind.Page, status: ContentStatus.Published);
        child.ParentId = parent.Id;
        await admin.Content.SaveAsync(child);

        var resolved = await admin.Content.ResolvePublishedPagePathAsync(["母頁面", "子頁面"]);
        Assert.NotNull(resolved);
        Assert.Equal(["母頁面", "子頁面"], resolved!.Segments);
        Assert.Equal("子頁面", resolved.Content.Slug);

        // 祖先未發布：子頁退回平面路徑。
        var hiddenParent = NewContent("隱藏母頁", kind: ContentKind.Page, status: ContentStatus.Draft);
        await admin.Content.SaveAsync(hiddenParent);
        var orphan = NewContent("孤儿子頁", kind: ContentKind.Page, status: ContentStatus.Published);
        orphan.ParentId = hiddenParent.Id;
        await admin.Content.SaveAsync(orphan);
        var fallback = await admin.Content.ResolvePublishedPagePathAsync(["孤儿子頁"]);
        Assert.NotNull(fallback);
        Assert.Equal(["孤儿子頁"], fallback!.Segments);

        // sitemap 使用完整階層路徑，平面子頁維持平面。
        var sitemap = await admin.Content.SitemapAsync();
        Assert.Contains(sitemap, x => x.Path == "/pages/母頁面/子頁面");
        Assert.Contains(sitemap, x => x.Path == "/pages/孤儿子頁");
    }

    [PostgresFact]
    public async Task Site_serves_hierarchical_urls_and_redirects_flat_ones()
    {
        using var admin = await App.AdministratorAsync();
        var parent = NewContent("頂層頁面", kind: ContentKind.Page, status: ContentStatus.Published);
        await admin.Content.SaveAsync(parent);
        var child = NewContent("深層頁面", kind: ContentKind.Page, status: ContentStatus.Published);
        child.ParentId = parent.Id;
        await admin.Content.SaveAsync(child);
        await admin.Content.SaveAsync(NewContent("普通文章", status: ContentStatus.Published));

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        // 平面子頁網址 301 到完整階層路徑。
        using var flat = await client.GetAsync("/pages/深層頁面");
        Assert.Equal(HttpStatusCode.MovedPermanently, flat.StatusCode);
        Assert.Equal("/pages/頂層頁面/深層頁面", Uri.UnescapeDataString(flat.Headers.Location?.ToString() ?? ""));

        // 完整階層網址正常渲染，導覽連結同樣指向階層網址。
        using var deep = await client.GetAsync("/pages/頂層頁面/深層頁面");
        Assert.Equal(HttpStatusCode.OK, deep.StatusCode);
        var html = WebUtility.HtmlDecode(await deep.Content.ReadAsStringAsync());
        Assert.Contains("深層頁面", html, StringComparison.Ordinal);
        // 連結屬性為 percent-encoded 的網址，比對編碼後形式。
        var hierarchyHref = "/pages/" + Uri.EscapeDataString("頂層頁面") + "/" + Uri.EscapeDataString("深層頁面");
        Assert.Contains(hierarchyHref, html, StringComparison.Ordinal);
        Assert.Contains("canonical", html, StringComparison.Ordinal);

        // 錯誤的中段路徑 404；頂層頁面自身維持平面網址。
        using var wrong = await client.GetAsync("/pages/錯誤中段/深層頁面");
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var top = await client.GetAsync("/pages/頂層頁面");
        Assert.Equal(HttpStatusCode.OK, top.StatusCode);

        // 文章路由不受影響；oEmbed 接受三段式頁面網址。
        using var post = await client.GetAsync("/posts/普通文章");
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        using var embed = await client.GetAsync("/oembed?url=" + Uri.EscapeDataString("/pages/頂層頁面/深層頁面"));
        Assert.Equal(HttpStatusCode.OK, embed.StatusCode);
        Assert.Equal("深層頁面", (string)JsonNode.Parse(await embed.Content.ReadAsStringAsync())!["title"]!);
    }

    [PostgresFact]
    public async Task Private_ancestor_falls_back_to_flat_path_and_never_leaks_its_slug()
    {
        using var admin = await App.AdministratorAsync();
        var secret = NewContent("私密母頁", kind: ContentKind.Page, status: ContentStatus.Published);
        secret.IsPrivate = true;
        await admin.Content.SaveAsync(secret);
        var child = NewContent("私密母頁子頁", kind: ContentKind.Page, status: ContentStatus.Published);
        child.ParentId = secret.Id;
        await admin.Content.SaveAsync(child);

        // 解析服務：私密祖先視同未發布，子頁退回平面（與 sitemap／導覽一致）。
        var resolved = await admin.Content.ResolvePublishedPagePathAsync(["私密母頁子頁"]);
        Assert.NotNull(resolved);
        Assert.Equal(["私密母頁子頁"], resolved!.Segments);

        var sitemap = await admin.Content.SitemapAsync();
        Assert.Contains(sitemap, x => x.Path == "/pages/私密母頁子頁");
        Assert.DoesNotContain(sitemap, x => x.Path.Contains("私密母頁/", StringComparison.Ordinal) || x.Path == "/pages/私密母頁");

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        // 平面子頁直接 200（不轉址）；含私密段的路徑 404——私密 slug 不出現在公開網址。
        using var flat = await client.GetAsync("/pages/私密母頁子頁");
        Assert.Equal(HttpStatusCode.OK, flat.StatusCode);
        using var leaked = await client.GetAsync("/pages/私密母頁/私密母頁子頁");
        Assert.Equal(HttpStatusCode.NotFound, leaked.StatusCode);
    }

    [PostgresFact]
    public async Task Deep_page_paths_resolve_oembed_and_preview_redirects_in_one_hop()
    {
        using var admin = await App.AdministratorAsync();
        var top = NewContent("三層頂", kind: ContentKind.Page, status: ContentStatus.Published);
        await admin.Content.SaveAsync(top);
        var mid = NewContent("三層中", kind: ContentKind.Page, status: ContentStatus.Published);
        mid.ParentId = top.Id;
        await admin.Content.SaveAsync(mid);
        var leaf = NewContent("三層葉", kind: ContentKind.Page, status: ContentStatus.Published);
        leaf.ParentId = mid.Id;
        await admin.Content.SaveAsync(leaf);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        using var deep = await client.GetAsync("/pages/三層頂/三層中/三層葉");
        Assert.Equal(HttpStatusCode.OK, deep.StatusCode);

        // oEmbed 接受最深層頁面並回完整階層網址；錯誤中段與路由同樣 404。
        using var embed = await client.GetAsync("/oembed?url=" + Uri.EscapeDataString("/pages/三層頂/三層中/三層葉"));
        Assert.Equal(HttpStatusCode.OK, embed.StatusCode);
        var payload = JsonNode.Parse(await embed.Content.ReadAsStringAsync())!;
        Assert.Equal("三層葉", (string)payload["title"]!);
        Assert.Contains("/pages/三層頂/三層中/三層葉", Uri.UnescapeDataString((string)payload["url"]!), StringComparison.Ordinal);
        using var wrongMid = await client.GetAsync("/oembed?url=" + Uri.EscapeDataString("/pages/三層頂/錯層/三層葉"));
        Assert.Equal(HttpStatusCode.NotFound, wrongMid.StatusCode);

        // 預覽連結在內容已發布後直接 301 到完整階層網址（單跳，不再經平面網址轉兩次）。
        var token = await admin.Content.CyclePreviewTokenAsync(leaf.Id!.Value);
        using var preview = await client.GetAsync("/preview/" + token);
        Assert.Equal(HttpStatusCode.MovedPermanently, preview.StatusCode);
        var expected = "/pages/" + string.Join("/", new[] { "三層頂", "三層中", "三層葉" }.Select(Uri.EscapeDataString));
        Assert.Equal(expected, preview.Headers.Location?.ToString());
    }
}
