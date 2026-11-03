namespace Origami.Tests;

/// <summary>
/// 受控小工具與自訂 CSS：清理器邊界、小工具 CRUD／排序／公開投影／跨站隔離、
/// 設定清理落庫與匯出匯入往返。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WidgetAndCustomCssTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [Fact]
    public void Css_sanitizer_rejects_dangerous_constructs_and_strips_comments()
    {
        // 危險構造一律拒絕，且不得被註解或大小寫繞過。
        foreach (var dangerous in new[]
                 {
                     "@import url('https://evil.test/x.css');",
                     "a { background: url(javascript:alert(1)); }",
                     "a { width: expression(alert(1)); }",
                     "a { behavior: url(evil.htc); }",
                     "a { content: '</style><script>' }",
                     "a { color: red\\; }",
                     "@font-face { font-family: x; src: url('https://evil.test/f.woff2'); }",
                     "@charset 'utf-8';"
                 })
        {
            Assert.False(CssSanitizer.TryClean(dangerous, out _), dangerous);
            Assert.Throws<CmsException>(() => CssSanitizer.CleanOrThrow(dangerous));
        }

        // 註解先移除再掃描：藏在註解裡的危險 token 不會被帶進清理結果，也不會誤傷其他規則。
        Assert.True(CssSanitizer.TryClean("a { color: red } /* javascript: */", out var cleaned));
        Assert.DoesNotContain("javascript", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("/*", cleaned, StringComparison.Ordinal);

        // 一般排版與媒體查詢可通過，且原樣保留。
        var ok = "@media (min-width: 40rem) { .site-header { letter-spacing: .02em !important; } }";
        Assert.Equal(ok, CssSanitizer.CleanOrThrow(ok));
        Assert.Equal("", CssSanitizer.CleanOrThrow("  "));
        Assert.Equal("", CssSanitizer.CleanOrThrow(null));

        // 長度上限。
        var tooLong = new string('a', Limits.CustomCss + 1);
        Assert.False(CssSanitizer.TryClean(tooLong, out _));
    }

    [PostgresFact]
    public async Task Widgets_validate_crud_reorder_and_public_projection()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "分類甲", Slug = "cat-a" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "標籤乙", Slug = "tag-b" });
        var published = NewContent("已發布文章", status: ContentStatus.Published);
        published.CategoryIds = [category.Id!.Value];
        published.TagIds = [tag.Id!.Value];
        await admin.Content.SaveAsync(published);
        await admin.Content.SaveAsync(NewContent("草稿文章"));

        var widgets = admin.Services.GetRequiredService<WidgetService>();

        // 邊界：篇數、文字長度、標題與空文字都由服務層把關。
        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.RecentPosts, Count = 0 }));
        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.RecentPosts, Count = Limits.WidgetRecentPosts + 1 }));
        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.Text, Markdown = " " }));
        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.Text, Title = new string('標', Limits.WidgetTitle + 1), Markdown = "x" }));

        var recent = await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.RecentPosts, Count = 1, Title = "最新" });
        var text = await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.Text, Markdown = "你好 **世界**" });
        var categories = await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Footer, Type = WidgetType.Categories });
        var tags = await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Footer, Type = WidgetType.Tags });
        var search = await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Footer, Type = WidgetType.Search, IsVisible = false });

        // 區域不可就地變更；並發版本錯誤也須拒絕。
        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
            { Id = recent.Id, Area = WidgetArea.Footer, Type = WidgetType.RecentPosts, Version = recent.Version }));
        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
            { Id = recent.Id, Area = WidgetArea.Sidebar, Type = WidgetType.RecentPosts, Version = Guid.NewGuid() }));

        var list = await widgets.ListAsync();
        Assert.Equal(5, list.Count);
        Assert.Equal(2, list.Count(x => x.Area == WidgetArea.Sidebar));

        var publicWidgets = await widgets.PublicAsync();
        Assert.Equal(4, publicWidgets.Count); // 隱藏的搜尋框不投影
        var publicRecent = Assert.Single(publicWidgets, x => x.Type == WidgetType.RecentPosts);
        Assert.Equal("最新", publicRecent.Title);
        var post = Assert.Single(publicRecent.RecentPosts); // 篇數 1：只含已發布文章
        Assert.Equal("已發布文章", post.Title);
        Assert.Equal(WidgetArea.Footer, Assert.Single(publicWidgets, x => x.Type == WidgetType.Categories).Area);
        Assert.Contains(publicWidgets.SelectMany(x => x.Categories), x => x.Slug == "cat-a");
        Assert.Contains(publicWidgets.SelectMany(x => x.Tags), x => x.Slug == "tag-b");

        // 側欄內排序：文字調到最新文章之前。
        var results = await widgets.ReorderAsync(WidgetArea.Sidebar,
            [(text.Id, text.Version), (recent.Id, recent.Version)]);
        Assert.All(results, x => Assert.True(x.Succeeded));
        var sidebarOrder = (await widgets.ListAsync()).Where(x => x.Area == WidgetArea.Sidebar).ToList();
        Assert.Equal(text.Id, sidebarOrder[0].Id);

        // 刪除後不再投影。
        await widgets.DeleteAsync(search.Id, search.Version);
        Assert.DoesNotContain(await widgets.ListAsync(), x => x.Id == search.Id);
    }

    [PostgresFact]
    public async Task Widgets_require_sign_in_and_are_isolated_per_site()
    {
        using (var anonymous = App.Anonymous())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                anonymous.Services.GetRequiredService<WidgetService>().ListAsync());
        }

        var site = await CreateSiteAsync("widgetsite");
        using var admin = await App.AdministratorAsync(site.Id);
        var widgets = admin.Services.GetRequiredService<WidgetService>();
        await widgets.SaveAsync(new WidgetService.WidgetInput { Area = WidgetArea.Footer, Type = WidgetType.Search });

        using var defaultAdmin = await App.AdministratorAsync();
        Assert.Empty(await defaultAdmin.Services.GetRequiredService<WidgetService>().ListAsync());
    }

    [PostgresFact]
    public async Task Custom_css_is_sanitized_on_save_and_export_import_round_trips()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();

        // 危險構造在儲存時即被拒絕。
        settings.CustomCss = "a { background: url(javascript:alert(1)) }";
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(settings));

        // 安全樣式清理後落庫：註解被移除、其餘原樣保留。
        settings.CustomCss = "/* 註解 */ .site-header { letter-spacing: .02em !important; }";
        var saved = await admin.Settings.SaveAsync(settings);
        Assert.Equal(".site-header { letter-spacing: .02em !important; }", saved.CustomCss);

        // 小工具與自訂 CSS 都隨匯出檔往返。
        var widgets = admin.Services.GetRequiredService<WidgetService>();
        await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Footer, Type = WidgetType.Text, Title = "關於", Markdown = " Powered by Origami " });

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        using var input = new MemoryStream(buffer.ToArray());
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        await using var db = await App.CreateContextAsync();
        var restored = await db.SiteWidgets.AsNoTracking().ToListAsync();
        var item = Assert.Single(restored);
        Assert.Equal(WidgetArea.Footer, item.Area);
        Assert.Equal(WidgetType.Text, item.Type);
        Assert.Equal("關於", item.Title);
        Assert.Contains("Powered by Origami", item.ConfigJson);
        Assert.Equal(saved.CustomCss, (await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1)).CustomCss);
    }

    [PostgresFact]
    public async Task Recent_comments_widget_shows_only_approved_comments_on_public_posts()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        settings.CommentAutoApprove = false;
        await admin.Settings.SaveAsync(settings);

        var post = await admin.Content.SaveAsync(NewContent("評論小工具文章", status: ContentStatus.Published));

        var comments = admin.Services.GetRequiredService<CommentService>();
        await comments.PostAsync(post.Id!.Value, new CommentService.CommentPostInput("訪客甲", null, "**第一則**已核可", false));
        await comments.PostAsync(post.Id!.Value, new CommentService.CommentPostInput("訪客乙", null, "待審留言", false));
        var queue = await comments.ListAdminAsync(CommentStatus.Pending, ct: default);
        foreach (var item in queue.Items.Where(x => x.AuthorName != "訪客乙"))
            await comments.ModerateAsync(item.Id, CommentStatus.Approved, item.Version);

        var widgets = admin.Services.GetRequiredService<WidgetService>();
        await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.RecentComments, Count = 2, Title = "最新回應" });

        var widget = Assert.Single(await widgets.PublicAsync());
        var approved = Assert.Single(widget.RecentComments); // 僅已核可的評論；待審不計
        Assert.Equal("評論小工具文章", approved.PostTitle);
        Assert.Equal("訪客甲", approved.AuthorName);
        Assert.Equal("第一則已核可", approved.Excerpt); // Markdown 語法剝除

        // 關閉本站評論後，小工具不再輸出任何評論。
        settings.CommentsEnabled = false;
        await admin.Settings.SaveAsync(settings);
        widget = Assert.Single(await widgets.PublicAsync());
        Assert.Empty(widget.RecentComments);
    }

    [PostgresFact]
    public async Task Text_widget_accepts_full_limit_of_non_ascii_markdown()
    {
        using var admin = await App.AdministratorAsync();
        var widgets = admin.Services.GetRequiredService<WidgetService>();

        // 非 ASCII 字元序列化為 \uXXXX（每字 6 字元）：10,000 字 CJK 約 60,050 字元，
        // 欄位上限（Limits.WidgetConfig）必須容納，否則會以資料庫例外而非友好訊息失敗。
        var saved = await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.Text, Markdown = new string('寬', Limits.WidgetMarkdown) });
        Assert.Equal(Limits.WidgetMarkdown, saved.Markdown.Length);

        var view = Assert.Single(await widgets.ListAsync(), x => x.Id == saved.Id);
        Assert.Equal(Limits.WidgetMarkdown, view.Markdown.Length);
        var publicWidget = Assert.Single(await widgets.PublicAsync(), x => x.Id == saved.Id);
        Assert.Equal(Limits.WidgetMarkdown, publicWidget.Markdown.Length);

        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Footer, Type = WidgetType.Text, Markdown = new string('寬', Limits.WidgetMarkdown + 1) }));
    }

    [PostgresFact]
    public async Task Public_projection_clamps_invalid_counts_from_database()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("夾持文章", status: ContentStatus.Published));
        var widgets = admin.Services.GetRequiredService<WidgetService>();
        await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.RecentPosts, Count = 3 });

        // 模擬未經服務層驗證的匯入檔：直接改寫組態為負數篇數，公開投影不得拋出例外。
        await using var db = await App.CreateContextAsync();
        var row = await db.SiteWidgets.SingleAsync();
        row.ConfigJson = """{"count":-3}""";
        await db.SaveChangesAsync();

        var widget = Assert.Single(await widgets.PublicAsync());
        Assert.Equal("夾持文章", Assert.Single(widget.RecentPosts).Title); // 夾持為 1 篇，而非 Take(負數) 拋出
    }

    [PostgresFact]
    public async Task Feed_fetch_degrades_on_oversize_or_interrupted_streams()
    {
        using var admin = await App.AdministratorAsync();
        var access = admin.Services.GetRequiredService<AdminAccess>();
        var widgets = admin.Services.GetRequiredService<WidgetService>();
        await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.RssFeed, Url = "https://big.example.test/feed", Count = 5 });
        await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Footer, Type = WidgetType.RssFeed, Url = "https://broken.example.test/feed", Count = 5 });

        // 超限串流：無限供給位元組，抓取須在上限處立即中止；中斷串流：下載途中 IOException。
        // 兩者都降級為空清單，不得讓公開頁面失敗。
        var degraded = new WidgetService(App.ContextFactory, access,
            new StubHttpClientFactory(url => url.Contains("big.example.test", StringComparison.Ordinal)
                ? new EndlessZeroStream() : new InterruptedStream()),
            new MemoryCache(new MemoryCacheOptions()));
        var result = await degraded.PublicAsync();
        Assert.Equal(2, result.Count(x => x.Type == WidgetType.RssFeed));
        Assert.All(result.Where(x => x.Type == WidgetType.RssFeed), x => Assert.Empty(x.RssItems));
    }

    [Fact]
    public void Rss_feed_parser_supports_rss2_and_atom_and_rejects_dtd()
    {
        var rss = """
            <?xml version="1.0"?><rss version="2.0"><channel><title>t</title>
            <item><title>甲</title><link>https://a.test/1</link><pubDate>Mon, 26 Oct 2026 08:00:00 GMT</pubDate></item>
            <item><title></title><link>https://a.test/2</link></item>
            <item><title>乙</title><link>javascript:alert(1)</link></item>
            </channel></rss>
            """;
        var items = WidgetService.ParseFeed(new MemoryStream(Encoding.UTF8.GetBytes(rss)), 5);
        var single = Assert.Single(items); // 空標題與非 well-formed 連結都被過濾
        Assert.Equal("甲", single.Title);
        Assert.Equal("https://a.test/1", single.Url);
        Assert.NotNull(single.PublishedAt);

        var atom = """
            <?xml version="1.0"?><feed xmlns="http://www.w3.org/2005/Atom"><title>t</title>
            <entry><title>丙</title><link href="https://a.test/3"/><updated>2026-10-26T08:00:00Z</updated></entry>
            </feed>
            """;
        Assert.Equal("丙", Assert.Single(WidgetService.ParseFeed(new MemoryStream(Encoding.UTF8.GetBytes(atom)), 5)).Title);

        // DOCTYPE 一律拒絕（XXE 防護）：Prohibit 在碰到 DTD 時拋出 XmlException。
        var xxe = """<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><rss></rss>""";
        Assert.Throws<XmlException>(() =>
            WidgetService.ParseFeed(new MemoryStream(Encoding.UTF8.GetBytes(xxe)), 5));
    }

    [PostgresFact]
    public async Task Rss_feed_widget_validates_public_https_urls_and_degrades_offline()
    {
        using var admin = await App.AdministratorAsync();
        var widgets = admin.Services.GetRequiredService<WidgetService>();

        foreach (var bad in new[] { "http://example.com/feed", "https://localhost/feed", "https://127.0.0.1/feed",
                     "https://192.168.1.1/feed", "notaurl", "" })
        {
            await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
                { Area = WidgetArea.Sidebar, Type = WidgetType.RssFeed, Url = bad }));
        }

        var saved = await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Sidebar, Type = WidgetType.RssFeed, Url = "https://external.example.test/feed.xml", Count = 3 });
        Assert.Equal("https://external.example.test/feed.xml", saved.Url);
        Assert.Equal(3, saved.Count);

        // 抓取失敗（離線／DNS 不存在）以空清單降級，不讓小工具拖垮頁面。
        var offline = new WidgetService(App.ContextFactory, admin.Services.GetRequiredService<AdminAccess>(),
            new FailingHttpClientFactory(), new MemoryCache(new MemoryCacheOptions()));
        var widget = Assert.Single(await offline.PublicAsync());
        Assert.Equal(WidgetType.RssFeed, widget.Type);
        Assert.Empty(widget.RssItems);
    }

    private sealed class FailingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FailingHandler());

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                throw new HttpRequestException("offline");
        }
    }

    /// <summary>依網址提供串流的測試用 HttpClient 工廠：回應一律 200，內容來自指定串流。</summary>
    private sealed class StubHttpClientFactory(Func<string, Stream> streams) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RoutingHandler(streams));

        private sealed class RoutingHandler(Func<string, Stream> streams) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StreamContent(streams(request.RequestUri!.AbsoluteUri)) });
        }
    }

    /// <summary>無限供給零位元組的串流：驗證超過位元組上限時抓取立即中止，不會無限讀取。</summary>
    private sealed class EndlessZeroStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>首段讀取成功後即拋 IOException：模擬下載中途連線中斷。</summary>
    private sealed class InterruptedStream : Stream
    {
        private bool _sentOnce;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_sentOnce) throw new IOException("connection reset");
            _sentOnce = true;
            Array.Clear(buffer, offset, count);
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
