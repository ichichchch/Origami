namespace Origami.Tests;

/// <summary>匯出範圍篩選：類型與分類過濾、不合法組合拒絕、篩選匯出可還原為部分內容。</summary>
[Collection(PostgresCollection.Name)]
public sealed class ExportFilterTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Kind_and_category_filters_narrow_the_exported_contents()
    {
        using var admin = await App.AdministratorAsync();
        var cat = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "篩選分類" });
        var other = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "其他分類" });
        var inCat = NewContent("分類內文章", status: ContentStatus.Published);
        inCat.CategoryIds = [cat.Id!.Value];
        await admin.Content.SaveAsync(inCat);
        var outCat = NewContent("分類外文章", status: ContentStatus.Published);
        outCat.CategoryIds = [other.Id!.Value];
        await admin.Content.SaveAsync(outCat);
        await admin.Content.SaveAsync(NewContent("獨立頁面", kind: ContentKind.Page, status: ContentStatus.Published));

        var export = admin.Services.GetRequiredService<ExportService>();

        using var all = new MemoryStream();
        await export.WriteAsync(all);
        Assert.Equal(3, JsonNode.Parse(all.ToArray())!["contents"]!.AsArray().Count);

        using var posts = new MemoryStream();
        await export.WriteAsync(posts, ContentKind.Post);
        var postsNode = JsonNode.Parse(posts.ToArray())!["contents"]!.AsArray();
        Assert.Equal(2, postsNode.Count);
        Assert.All(postsNode, node => Assert.Equal("post", (string?)node!["kind"]));

        using var filtered = new MemoryStream();
        await export.WriteAsync(filtered, ContentKind.Post, "篩選分類");
        var filteredNode = JsonNode.Parse(filtered.ToArray())!["contents"]!.AsArray();
        var filteredItem = Assert.Single(filteredNode);
        Assert.NotNull(filteredItem);
        Assert.Equal("分類內文章", (string?)filteredItem["title"]);

        using var pages = new MemoryStream();
        await export.WriteAsync(pages, ContentKind.Page);
        var pageItem = Assert.Single(JsonNode.Parse(pages.ToArray())!["contents"]!.AsArray());
        Assert.NotNull(pageItem);
        Assert.Equal("獨立頁面", (string?)pageItem["title"]);

        // 分類篩選僅與文章匯出相容；其他組合明確拒絕。
        await Assert.ThrowsAsync<CmsException>(() => export.WriteAsync(new MemoryStream(), ContentKind.Page, "篩選分類"));
    }

    [PostgresFact]
    public async Task Web_download_returns_a_json_file_with_the_requested_content_scope()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("Exported post"));
        await admin.Content.SaveAsync(NewContent("Excluded page", kind: ContentKind.Page));
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var login = await HttpSecurityTests.LoginAsync(client, password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var response = await client.GetAsync("/admin/export?kind=post");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document!["contents"]!.AsArray());
        Assert.Equal("Exported post", (string?)item!["title"]);
    }

    [PostgresFact]
    public async Task Filtered_export_restores_as_a_partial_import()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("篩選保留文章", status: ContentStatus.Published));
        await admin.Content.SaveAsync(NewContent("將被過濾的頁面", kind: ContentKind.Page, status: ContentStatus.Published));

        var export = admin.Services.GetRequiredService<ExportService>();
        using var postsOnly = new MemoryStream();
        await export.WriteAsync(postsOnly, ContentKind.Post);

        var importer = admin.Services.GetRequiredService<ImportService>();
        using (var input = new MemoryStream(postsOnly.ToArray()))
        {
            await importer.ApplyAsync(input, confirmed: true);
        }

        await using var db = await App.CreateContextAsync();
        Assert.True(await db.Contents.AsNoTracking().AnyAsync(x => x.Slug == "篩選保留文章"));
        Assert.False(await db.Contents.AsNoTracking().AnyAsync(x => x.Kind == ContentKind.Page));
    }
}
