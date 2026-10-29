namespace Origami.Tests;

/// <summary>
/// 進階 SEO：自訂 meta 描述、noindex（robots 標記與 sitemap 排除）、結構化資料。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SeoFieldsTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [Fact]
    public void Content_summary_preserves_the_constructor_update_time()
    {
        var updated = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var summary = new ContentSummary(Guid.NewGuid(), ContentKind.Post, "Updated post", "updated-post",
            ContentStatus.Published, updated, updated.AddDays(-1), Guid.NewGuid());
        Assert.Equal(updated, summary.UpdatedAt);
        var restored = JsonSerializer.Deserialize<ContentSummary>(JsonSerializer.Serialize(summary));
        Assert.NotNull(restored);
        Assert.Equal(updated, restored.UpdatedAt);
    }

    [PostgresFact]
    public async Task Meta_description_and_noindex_are_stored_and_exposed()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("SEO 文章", status: ContentStatus.Published);
        post.Excerpt = "摘要文字";
        post.MetaDescription = "自訂的搜尋結果描述";
        post.NoIndex = true;
        var saved = await admin.Content.SaveAsync(post);

        var edit = await admin.Content.GetEditAsync(saved.Id!.Value);
        Assert.Equal("自訂的搜尋結果描述", edit.MetaDescription);
        Assert.True(edit.NoIndex);

        var detail = await admin.Content.GetPublishedAsync(ContentKind.Post, saved.Slug!);
        Assert.NotNull(detail);
        Assert.Equal("自訂的搜尋結果描述", detail.MetaDescription);
        Assert.True(detail.NoIndex);

        // noindex 的內容不列入 sitemap，但仍可公開閱讀。
        var sitemap = await admin.Content.SitemapAsync();
        Assert.DoesNotContain(sitemap, x => x.Path.Contains(saved.Slug!, StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task Public_pages_emit_robots_and_structured_data()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("結構化資料文章", status: ContentStatus.Published);
        post.MetaDescription = "結構化描述";
        post.NoIndex = true;
        await admin.Content.SaveAsync(post);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var response = await client.GetAsync("/posts/" + Uri.EscapeDataString("結構化資料文章"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        // head 內的非 ASCII 字元與 + 會被編碼成 entity；比對前先解碼。
        var decoded = WebUtility.HtmlDecode(html);
        // noindex robots 標記與 schema.org 結構化資料都應出現在 head。
        Assert.Contains("name=\"robots\"", decoded);
        Assert.Contains("noindex", decoded);
        Assert.Contains("application/ld+json", decoded);
        Assert.Contains("schema.org", decoded);
        Assert.Contains("結構化描述", decoded);
    }

    [PostgresFact]
    public async Task Meta_description_survives_export_and_import()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("SEO 匯出文章", status: ContentStatus.Published);
        post.MetaDescription = "匯出後仍保留的描述";
        post.NoIndex = true;
        await admin.Content.SaveAsync(post);

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        using var input = new MemoryStream(buffer.ToArray());
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        await using var db = await App.CreateContextAsync();
        var restored = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == "seo-匯出文章");
        Assert.Equal("匯出後仍保留的描述", restored.MetaDescription);
        Assert.True(restored.NoIndex);
    }
}
