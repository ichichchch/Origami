namespace Origami.Tests;

/// <summary>
/// 遷移與運營批次：WXR 匯入記錄 wp-{post_id} 轉址（舊固定連結 /archives/{id} 301 的資料來源）、
/// 分類手動排序貫通、前台瀏覽次數開關與 IndexNow 金鑰驗證。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationAndOpsTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Wxr = """
        <?xml version="1.0" encoding="UTF-8" ?>
        <rss version="2.0"
          xmlns:content="http://purl.org/rss/1.0/modules/content/"
          xmlns:dc="http://purl.org/dc/elements/1.1/"
          xmlns:wp="http://wordpress.org/export/1.2/">
          <channel>
            <title>舊站</title>
            <item>
              <title><![CDATA[舊文]]></title>
              <dc:creator><![CDATA[ICHI]]></dc:creator>
              <content:encoded><![CDATA[<p>內容</p>]]></content:encoded>
              <wp:post_id>4396</wp:post_id>
              <wp:post_date><![CDATA[2025-09-01 10:00:00]]></wp:post_date>
              <wp:post_name><![CDATA[old-token-post]]></wp:post_name>
              <wp:status><![CDATA[publish]]></wp:status>
              <wp:post_type><![CDATA[post]]></wp:post_type>
            </item>
          </channel>
        </rss>
        """;

    [PostgresFact]
    public async Task Wxr_import_records_legacy_post_id_redirect()
    {
        using var admin = await App.AdministratorAsync();
        var wxr = admin.Services.GetRequiredService<WxrImportService>();
        var result = await wxr.ImportAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Wxr)));
        Assert.Equal(1, result.Imported);

        await using var db = await App.CreateContextAsync();
        var redirect = await db.SlugRedirects.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OldSlug == "wp-4396");
        Assert.NotNull(redirect);
        var target = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == redirect!.ContentId);
        Assert.Equal("old-token-post", target.Slug);
        Assert.Equal(ContentStatus.Published, target.Status);
        Assert.Equal("ICHI", target.AuthorName);
        // 署名代稱同步落地：前台作者彙整頁 /author/{slug} 可達。
        Assert.Equal("ichi", target.AuthorSlug);
    }

    [PostgresFact]
    public async Task Reimport_does_not_duplicate_legacy_redirect()
    {
        using var admin = await App.AdministratorAsync();
        var wxr = admin.Services.GetRequiredService<WxrImportService>();
        await wxr.ImportAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Wxr)));
        // 第二次匯入因代稱重複被略過，不應拋出轉址唯一約束錯誤。
        var second = await wxr.ImportAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Wxr)));
        Assert.Equal(0, second.Imported);

        await using var db = await App.CreateContextAsync();
        Assert.Equal(1, await db.SlugRedirects.AsNoTracking().CountAsync(x => x.OldSlug == "wp-4396"));
    }

    [PostgresFact]
    public async Task Taxonomy_sort_order_roundtrips_and_orders_listing()
    {
        using var admin = await App.AdministratorAsync();
        var taxonomies = admin.Services.GetRequiredService<TaxonomyService>();
        await taxonomies.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "乙類", Slug = "cat-b", SortOrder = 5 });
        await taxonomies.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "甲類", Slug = "cat-a", SortOrder = 1 });
        await taxonomies.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "丙類", Slug = "cat-c", SortOrder = 9 });

        var page = await taxonomies.ListAsync(TaxonomyKind.Category, page: 1, pageSize: 10);
        var names = string.Join(",", page.Items.Select(x => x.Name));
        Assert.StartsWith("甲類", names, StringComparison.Ordinal);
        Assert.EndsWith("丙類", names, StringComparison.Ordinal);
        Assert.Equal(1, page.Items.Single(x => x.Slug == "cat-a").SortOrder);

        var edited = page.Items.Single(x => x.Slug == "cat-c");
        var saved = await taxonomies.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit
            { Id = edited.Id, Name = edited.Name, Slug = edited.Slug, Version = edited.Version, SortOrder = -3 });
        Assert.Equal(-3, saved.SortOrder);
        var reordered = await taxonomies.ListAsync(TaxonomyKind.Category, page: 1, pageSize: 10);
        Assert.Equal("丙類", reordered.Items[0].Name);

        var tagSaved = await taxonomies.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "標籤甲", Slug = "tag-a", SortOrder = 7 });
        Assert.Equal(7, tagSaved.SortOrder);
    }

    [PostgresFact]
    public async Task View_count_display_toggles_and_indexnow_key_is_validated()
    {
        using var admin = await App.AdministratorAsync();
        var settings = admin.Services.GetRequiredService<SettingsService>();
        var current = await settings.GetAsync();
        Assert.False(current.ShowViewCounts); // 預設關閉，不影響既有站台呈現。

        current.ShowViewCounts = true;
        var saved = await settings.SaveAsync(current);
        Assert.True(saved.ShowViewCounts);
        Assert.True((await settings.GetAsync()).ShowViewCounts);

        // 啟用 IndexNow 時金鑰必須是 8–128 個十六進位字元。
        var withKey = await settings.GetAsync();
        withKey.IndexNowEnabled = true;
        withKey.IndexNowKey = "not-hex!";
        await Assert.ThrowsAsync<CmsException>(() => settings.SaveAsync(withKey));

        var valid = await settings.GetAsync();
        valid.IndexNowEnabled = true;
        valid.IndexNowKey = "a1b2c3d4e5f60718";
        var enabled = await settings.SaveAsync(valid);
        Assert.True(enabled.IndexNowEnabled);
        Assert.Equal("a1b2c3d4e5f60718", (await settings.GetAsync()).IndexNowKey);
    }
}
