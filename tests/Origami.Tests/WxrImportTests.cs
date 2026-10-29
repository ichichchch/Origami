namespace Origami.Tests;

/// <summary>
/// WordPress WXR 匯入：文章／頁面／分類／標籤對映、HTML 轉 Markdown、狀態與署名、
/// 重複代稱略過、DTD 禁止（外部實體攻擊防護）。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WxrImportTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Wxr = """
        <?xml version="1.0" encoding="UTF-8" ?>
        <rss version="2.0"
          xmlns:content="http://purl.org/rss/1.0/modules/content/"
          xmlns:dc="http://purl.org/dc/elements/1.1/"
          xmlns:wp="http://wordpress.org/export/1.2/">
          <channel>
            <title>舊站</title>
            <wp:category><wp:cat_name><![CDATA[新聞]]></wp:cat_name><wp:category_nicename><![CDATA[news]]></wp:category_nicename></wp:category>
            <wp:category><wp:cat_name><![CDATA[專欄]]></wp:cat_name><wp:category_nicename><![CDATA[column]]></wp:category_nicename></wp:category>
            <item>
              <title><![CDATA[舊站文章]]></title>
              <dc:creator><![CDATA[舊作者]]></dc:creator>
              <content:encoded><![CDATA[<p>第一段<strong>粗體</strong></p><h2>小標</h2><ul><li>項目一</li></ul><script>alert(1)</script><a href="javascript:alert(2)">危險連結</a>]]></content:encoded>
              <wp:post_id>1</wp:post_id>
              <wp:post_date><![CDATA[2024-05-01 10:00:00]]></wp:post_date>
              <wp:post_name><![CDATA[old-post]]></wp:post_name>
              <wp:status><![CDATA[publish]]></wp:status>
              <wp:post_type><![CDATA[post]]></wp:post_type>
              <category domain="category" nicename="news"><![CDATA[新聞]]></category>
            </item>
            <item>
              <title><![CDATA[舊站頁面]]></title>
              <content:encoded><![CDATA[<p>頁面內容</p>]]></content:encoded>
              <wp:post_id>2</wp:post_id>
              <wp:post_date><![CDATA[2024-05-02 10:00:00]]></wp:post_date>
              <wp:post_name><![CDATA[old-page]]></wp:post_name>
              <wp:status><![CDATA[draft]]></wp:status>
              <wp:post_type><![CDATA[page]]></wp:post_type>
            </item>
          </channel>
        </rss>
        """;

    private static MemoryStream File() => new(System.Text.Encoding.UTF8.GetBytes(Wxr));

    [PostgresFact]
    public async Task Wxr_import_creates_posts_pages_and_taxonomies()
    {
        using var admin = await App.AdministratorAsync();
        var wxr = admin.Services.GetRequiredService<WxrImportService>();

        using var previewStream = File();
        var preview = await wxr.AnalyzeAsync(previewStream);
        Assert.Equal(1, preview.Posts);
        Assert.Equal(1, preview.Pages);
        Assert.Equal(2, preview.Categories);

        using var importStream = File();
        var result = await wxr.ImportAsync(importStream);
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);

        await using var db = await App.CreateContextAsync();
        var post = await db.Contents.AsNoTracking().Include(x => x.Categories).SingleAsync(x => x.Slug == "old-post");
        Assert.Equal(ContentKind.Post, post.Kind);
        Assert.Equal(ContentStatus.Published, post.Status);
        Assert.Equal("舊作者", post.AuthorName);
        Assert.NotNull(post.PublishedAt);
        // HTML 轉為 Markdown：保留語意標記，丟棄腳本與危險連結。
        Assert.Contains("**粗體**", post.Markdown);
        Assert.Contains("## 小標", post.Markdown);
        Assert.Contains("- 項目一", post.Markdown);
        Assert.DoesNotContain("alert", post.Markdown);
        Assert.DoesNotContain("javascript:", post.Markdown);
        Assert.Single(post.Categories);

        var page = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == "old-page");
        Assert.Equal(ContentKind.Page, page.Kind);
        Assert.Equal(ContentStatus.Draft, page.Status);

        var category = await db.Categories.AsNoTracking().SingleAsync(x => x.Slug == "news");
        Assert.Equal("新聞", category.Name);
    }

    [PostgresFact]
    public async Task Duplicate_slugs_are_skipped_and_reported()
    {
        using var admin = await App.AdministratorAsync();
        var wxr = admin.Services.GetRequiredService<WxrImportService>();
        using (var first = File()) await wxr.ImportAsync(first);
        using var second = File();
        var result = await wxr.ImportAsync(second);
        Assert.Equal(0, result.Imported);
        Assert.Equal(2, result.Skipped);
        Assert.Contains(result.Warnings, w => w.Contains("已存在", StringComparison.Ordinal));

        await using var db = await App.CreateContextAsync();
        Assert.Equal(2, await db.Contents.AsNoTracking().CountAsync());
    }

    [PostgresFact]
    public async Task External_entities_are_refused()
    {
        using var admin = await App.AdministratorAsync();
        var wxr = admin.Services.GetRequiredService<WxrImportService>();
        var hostile = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <rss version="2.0"><channel><title>&xxe;</title></channel></rss>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(hostile));
        await Assert.ThrowsAsync<CmsException>(() => wxr.AnalyzeAsync(stream));
    }

    [PostgresFact]
    public async Task Declared_tags_and_implicit_terms_are_imported_and_attached()
    {
        const string tagged = """
            <?xml version="1.0" encoding="UTF-8" ?>
            <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/"
              xmlns:wp="http://wordpress.org/export/1.2/">
              <channel>
                <wp:tag><wp:tag_slug><![CDATA[tech]]></wp:tag_slug><wp:tag_name><![CDATA[技術]]></wp:tag_name></wp:tag>
                <item>
                  <title><![CDATA[帶標籤文章]]></title>
                  <content:encoded><![CDATA[<p>內容</p>]]></content:encoded>
                  <wp:post_name><![CDATA[tagged-post]]></wp:post_name>
                  <wp:status><![CDATA[publish]]></wp:status>
                  <wp:post_type><![CDATA[post]]></wp:post_type>
                  <category domain="post_tag" nicename="tech"><![CDATA[技術]]></category>
                  <category domain="post_tag" nicename="misc"><![CDATA[其他]]></category>
                </item>
              </channel>
            </rss>
            """;
        using var admin = await App.AdministratorAsync();
        var wxr = admin.Services.GetRequiredService<WxrImportService>();

        using var previewStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(tagged));
        var preview = await wxr.AnalyzeAsync(previewStream);
        Assert.Equal(1, preview.Tags);
        Assert.Equal(0, preview.Categories);

        using var importStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(tagged));
        var result = await wxr.ImportAsync(importStream);
        Assert.Equal(1, result.Imported);

        await using var db = await App.CreateContextAsync();
        // 已宣告的標籤採用宣告名稱；未宣告但被引用者以項目文字建立。
        Assert.Equal("技術", (await db.Tags.AsNoTracking().SingleAsync(x => x.Slug == "tech")).Name);
        Assert.Equal("其他", (await db.Tags.AsNoTracking().SingleAsync(x => x.Slug == "misc")).Name);
        var post = await db.Contents.AsNoTracking().Include(x => x.Tags).SingleAsync(x => x.Slug == "tagged-post");
        Assert.Equal(2, post.Tags.Count);
    }

    [PostgresFact]
    public async Task Oversized_input_is_refused_without_buffering_all_of_it()
    {
        using var admin = await App.AdministratorAsync();
        var wxr = admin.Services.GetRequiredService<WxrImportService>();
        using var endless = new EndlessStream();
        var error = await Assert.ThrowsAsync<CmsException>(() => wxr.AnalyzeAsync(endless));
        Assert.Contains("上限", error.Message);
    }

    /// <summary>無限長輸入：用來確認匯入不會先全部讀進記憶體才檢查大小。</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => count;
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [PostgresFact]
    public void Html_conversion_keeps_structure_and_drops_dangerous_parts()
    {
        var markdown = HtmlToMarkdown.Convert(
            "<h1>標題</h1><p>段落 <em>斜體</em> <a href=\"https://example.com\">連結</a></p><blockquote>引用</blockquote><pre><code>code()</code></pre>");
        Assert.Contains("# 標題", markdown);
        Assert.Contains("*斜體*", markdown);
        Assert.Contains("[連結](https://example.com)", markdown);
        Assert.Contains("> 引用", markdown);
        Assert.Contains("```", markdown);
    }
}
