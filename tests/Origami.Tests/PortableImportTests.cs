namespace Origami.Tests;

/// <summary>Markdown／Medium 可攜匯入：詮釋資料、新增式匯入、HTML 安全轉換與 ZIP 邊界。</summary>
[Collection(PostgresCollection.Name)]
public sealed class PortableImportTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Markdown = """
        ---
        title: "遷移文章"
        slug: imported-post
        type: post
        status: publish
        categories: ["新聞", "技術"]
        tags: AI, "內容管理"
        lang: zh-Hant
        date: 2024-05-01T10:00:00Z
        description: 測試描述
        noindex: true
        sticky: true
        ---
        # 遷移文章

        第一段與 **重點**。

        ## 第二節

        - 項目
        """;

    private static MemoryStream MarkdownFile() =>
        new(System.Text.Encoding.UTF8.GetBytes(Markdown));

    private static async Task<MemoryStream> ZipFileAsync(params (string Name, string Text)[] entries)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var target = archive.CreateEntry(entry.Name).Open();
                await using (target)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(entry.Text);
                    await target.WriteAsync(bytes);
                }
            }
        }
        buffer.Position = 0;
        return buffer;
    }

    [PostgresFact]
    public async Task Markdown_front_matter_maps_status_taxonomy_dates_and_flags()
    {
        using var admin = await App.AdministratorAsync();
        using var analysis = MarkdownFile();
        var preview = await admin.Portable.AnalyzeAsync(analysis, "post.md");
        Assert.Equal(1, preview.Posts);
        Assert.Equal(2, preview.Categories);
        Assert.Equal(2, preview.Tags);

        using var input = MarkdownFile();
        var result = await admin.Portable.ApplyAsync(input, "post.md");
        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Warnings);

        await using var db = await App.CreateContextAsync();
        var post = await db.Contents.AsNoTracking()
            .Include(x => x.Categories).Include(x => x.Tags)
            .SingleAsync(x => x.Slug == "imported-post");
        Assert.Equal(ContentStatus.Published, post.Status);
        Assert.Equal("zh-Hant", post.Language);
        Assert.Equal("測試描述", post.MetaDescription);
        Assert.True(post.NoIndex);
        Assert.True(post.IsSticky);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero), post.PublishedAt);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero), post.CreatedAt);
        Assert.Equal(2, post.Categories.Count);
        Assert.Equal(2, post.Tags.Count);
        Assert.Contains("**重點**", post.Markdown);
    }

    [PostgresFact]
    public async Task Medium_html_is_converted_to_safe_markdown_without_downloading_images()
    {
        const string html = """
            <html><head><title>忽略這個標題</title></head><body>
              <h1 class="title">Medium 文章</h1><p class="subtitle">摘要文字</p>
              <article class="postArticle-content">
                <p>第一段 <strong>粗體</strong> <a href="https://example.com/a">連結</a>
                <a href="javascript:alert(1)">危險連結</a></p>
                <h2>小節</h2><ul><li>項目一</li><li>項目二</li></ul>
                <pre><code class="language-csharp">var x = 1;</code></pre>
                <figure><img src="https://example.com/photo.jpg" alt="照片"><figcaption>圖說</figcaption></figure>
                <script>alert(2)</script>
              </article>
            </body></html>
            """;
        using var admin = await App.AdministratorAsync();
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
        var preview = await admin.Portable.AnalyzeAsync(input, "medium.html");
        input.Position = 0;
        var result = await admin.Portable.ApplyAsync(input, "medium.html");
        Assert.Equal(1, result.Imported);

        await using var db = await App.CreateContextAsync();
        var post = await db.Contents.AsNoTracking().SingleAsync();
        Assert.Equal("Medium 文章", post.Title);
        Assert.Equal("摘要文字", post.Excerpt);
        Assert.Equal(ContentStatus.Draft, post.Status);
        Assert.Contains("**粗體**", post.Markdown);
        Assert.Contains("[連結](https://example.com/a)", post.Markdown);
        Assert.Contains("危險連結", post.Markdown);
        Assert.DoesNotContain("javascript:", post.Markdown);
        Assert.DoesNotContain("alert", post.Markdown);
        Assert.Contains("## 小節", post.Markdown);
        Assert.Contains("- 項目一", post.Markdown);
        Assert.Contains("```csharp", post.Markdown);
        Assert.Contains("![照片](https://example.com/photo.jpg)", post.Markdown);
    }

    [PostgresFact]
    public async Task Zip_imports_posts_and_pages_and_skips_duplicate_slugs()
    {
        const string page = "---\ntitle: 關於頁\nslug: about\ntype: page\ncategories: [不適用]\n---\n頁面內文。";
        const string duplicate = "---\ntitle: 重複\ntitle: 重複\nslug: imported-post\n---\n重複內容。";
        using var admin = await App.AdministratorAsync();
        using var archive = await ZipFileAsync(("posts/one.md", Markdown), ("pages/about.md", page), ("other.md", duplicate));
        var preview = await admin.Portable.AnalyzeAsync(archive, "export.zip");
        archive.Position = 0;
        Assert.Equal(1, preview.Posts);
        Assert.Equal(1, preview.Pages);
        Assert.Equal(2, preview.Categories);
        Assert.Contains(preview.Warnings, x => x.Contains("重複", StringComparison.Ordinal));

        archive.Position = 0;
        var result = await admin.Portable.ApplyAsync(archive, "export.zip");
        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(result.Warnings, x => x.Contains("是頁面", StringComparison.Ordinal));

        using var again = await ZipFileAsync(("one.md", Markdown));
        var repeated = await admin.Portable.ApplyAsync(again, "again.zip");
        Assert.Equal(0, repeated.Imported);
        Assert.Equal(1, repeated.Skipped);
        Assert.Contains(repeated.Warnings, x => x.Contains("已存在", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task Unpublished_future_date_is_imported_as_draft()
    {
        const string future = "---\ntitle: 排程文章\nslug: scheduled\nstatus: publish\ndate: 2999-01-01T00:00:00Z\n---\n尚未發布。";
        using var admin = await App.AdministratorAsync();
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(future));
        var result = await admin.Portable.ApplyAsync(input, "future.md");
        Assert.Equal(1, result.Imported);
        Assert.Contains(result.Warnings, x => x.Contains("未來", StringComparison.Ordinal));

        await using var db = await App.CreateContextAsync();
        var post = await db.Contents.AsNoTracking().SingleAsync();
        Assert.Equal(ContentStatus.Draft, post.Status);
        Assert.Null(post.PublishedAt);
    }

    [PostgresFact]
    public async Task Unsafe_zip_paths_and_oversized_expansion_are_refused()
    {
        using var admin = await App.AdministratorAsync();
        using var traversal = await ZipFileAsync(("../escape.md", "# 穿越\n內容"));
        await Assert.ThrowsAsync<CmsException>(() => admin.Portable.AnalyzeAsync(traversal, "bad.zip"));

        var compressed = new MemoryStream();
        using (var archive = new ZipArchive(compressed, ZipArchiveMode.Create, leaveOpen: true))
        {
            var target = archive.CreateEntry("large.md").Open();
            var chunk = new byte[1024 * 1024];
            Array.Fill(chunk, (byte)'a');
            for (var i = 0; i < Limits.PortableImportMaxBytes / chunk.Length + 1; i++)
                await target.WriteAsync(chunk);
        }
        compressed.Position = 0;
        var error = await Assert.ThrowsAsync<CmsException>(() => admin.Portable.AnalyzeAsync(compressed, "large.zip"));
        Assert.Contains("解壓", error.Message, StringComparison.Ordinal);
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.Contents.AsNoTracking().ToListAsync());
    }

    [PostgresFact]
    public async Task Editors_and_readers_cannot_import_content()
    {
        using var admin = await App.AdministratorAsync();
        var editor = await CreateMemberAsync(SiteRole.Editor, name: "editor");
        using var editorActor = await App.UserAsync(editor.Id);
        using var editorInput = MarkdownFile();
        await Assert.ThrowsAnyAsync<Exception>(() => editorActor.Portable.AnalyzeAsync(editorInput, "post.md"));

        var reader = await CreateMemberAsync(SiteRole.Reader, name: "reader");
        using var readerActor = await App.UserAsync(reader.Id);
        using var readerInput = MarkdownFile();
        await Assert.ThrowsAnyAsync<Exception>(() => readerActor.Portable.ApplyAsync(readerInput, "post.md"));
    }
}
