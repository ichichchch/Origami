"""把先前被 git checkout 覆蓋的前台功能（byline 連結、長文分頁、評論區、SEO）與語言切換器補回。"""
import io

p = 'src/Origami.Web/Components/Pages/ContentDetail.razor'
s = io.open(p, encoding='utf-8').read()

s = s.replace('@inject ContentUnlockThrottle UnlockThrottle',
              '@inject ContentUnlockThrottle UnlockThrottle\n@inject CommentThrottle CommentThrottle', 1)

s = s.replace('''            @if (_content.Kind == ContentKind.Post && _content.PublishedAt is { } published)
            {
                <meta property="article:published_time" content="@published.ToString("O")" />
            }''',
'''            @if (_content.Kind == ContentKind.Post && _content.PublishedAt is { } published)
            {
                <meta property="article:published_time" content="@published.ToString("O")" />
            }
            @if (_content.NoIndex)
            {
                <meta name="robots" content="noindex, nofollow" />
            }
            @if (StructuredData.Length > 0)
            {
                @* 結構化資料以原樣輸出；序列化器已逸出 < 與 >，不會形成可執行標記。 *@
                <script type="application/ld+json">@((MarkupString)StructuredData)</script>
            }''', 1)

s = s.replace('''            @if (!_locked && _content.Kind == ContentKind.Post && !string.IsNullOrWhiteSpace(_settings.AuthorDisplayName))
            {
                <p class="byline">作者 · @_settings.AuthorDisplayName</p>
            }''',
'''            @if (!_locked && _content.Kind == ContentKind.Post)
            {
                // 逐篇作者快照優先；未署名時退回站點設定的作者名稱。有署名代稱者連向作者彙整頁。
                var byline = string.IsNullOrWhiteSpace(_content.AuthorName) ? _settings.AuthorDisplayName : _content.AuthorName;
                @if (!string.IsNullOrWhiteSpace(byline))
                {
                    @if (!string.IsNullOrWhiteSpace(_content.AuthorSlug))
                    {
                        <p class="byline">作者 · <a href="@("/author/" + Uri.EscapeDataString(_content.AuthorSlug))">@byline</a></p>
                    }
                    else
                    {
                        <p class="byline">作者 · @byline</p>
                    }
                }
            }''', 1)

s = s.replace('''            <div class="prose">@((MarkupString)_html)</div>''',
'''            <div class="prose">@((MarkupString)_html)</div>
            @if (_pageTotal > 1)
            {
                <nav class="post-pagination" aria-label="內容分頁">
                    @if (_pageIndex > 1)
                    {
                        <a class="button button-secondary button-small" rel="prev" href="@PageHref(_pageIndex - 1)">上一頁</a>
                    }
                    <span class="muted">第 @_pageIndex 頁，共 @_pageTotal 頁</span>
                    @if (_pageIndex < _pageTotal)
                    {
                        <a class="button button-secondary button-small" rel="next" href="@PageHref(_pageIndex + 1)">下一頁</a>
                    }
                </nav>
            }''', 1)

s = s.replace('''    </article>
}''',
'''        @if (_settings.CommentsEnabled && !_locked && _content.Kind == ContentKind.Post)
        {
            <section class="comments" aria-labelledby="comments-title">
                <h2 id="comments-title">評論（@_comments.Count）</h2>
                @foreach (var comment in _comments)
                {
                    <article class="comment">
                        <p class="comment-meta">
                            @if (comment.Url.Length > 0)
                            {
                                <a href="@comment.Url" rel="nofollow noopener" target="_blank">@comment.AuthorName</a>
                            }
                            else
                            {
                                @comment.AuthorName
                            }
                            · @comment.CreatedAt.ToString("yyyy.MM.dd HH:mm") <span class="sr-only">UTC</span>
                        </p>
                        @* 訪客評論與內容走同一條 Markdown 安全渲染管線（停用 HTML、過濾危險協定）。 *@
                        <div class="comment-body">@((MarkupString)Markdown.Render(comment.Markdown))</div>
                    </article>
                }
                @if (_comments.Count == 0)
                {
                    <p class="muted">還沒有評論；成為第一位留言的訪客。</p>
                }
                @if (_commentPosted)
                {
                    <p class="notice notice-success" role="status">評論已送出；審核通過後會顯示在此頁。</p>
                }
                @if (_commentError is not null)
                {
                    <p class="notice notice-error" role="alert">@_commentError</p>
                }
                @* Static SSR 的 EditForm 內含防偽權杖，並在 POST 時驗證；評論端點經 BFF 通道寫入。 *@
                <EditForm Model="CommentForm" method="post" OnValidSubmit="PostCommentAsync" FormName="content-comment" Enhance="false">
                    <DataAnnotationsValidator />
                    <div class="field"><label for="comment-name">名稱 <span class="required">必填</span></label><input id="comment-name" name="CommentForm.AuthorName" maxlength="@Limits.CommentAuthor" required autocomplete="name" /></div>
                    <div class="field"><label for="comment-url">網址（選填）</label><input id="comment-url" name="CommentForm.Url" maxlength="@Limits.CommentUrl" autocomplete="url" placeholder="https://" /></div>
                    @* 蜜罐欄位對正常訪客隱藏；機器人填寫後該則評論靜默進入垃圾佇列。 *@
                    <div class="hp-field" aria-hidden="true"><label for="comment-website">請留空</label><input id="comment-website" name="CommentForm.Honeypot" tabindex="-1" autocomplete="off" /></div>
                    <div class="field"><label for="comment-body">評論 <span class="required">必填</span></label><textarea id="comment-body" name="CommentForm.Body" rows="4" maxlength="@Limits.CommentBody" required></textarea></div>
                    <button type="submit" class="button">送出評論</button>
                </EditForm>
            </section>
        }
    </article>
}''', 1)

s = s.replace('''    private IReadOnlyList<SemanticHit> _related = [];''',
'''    private IReadOnlyList<SemanticHit> _related = [];
    private List<CommentService.CommentView> _comments = [];
    private bool _commentPosted;
    private string? _commentError;
    private List<string> _pageSections = [""];
    private int _pageIndex = 1;
    private int _pageTotal = 1;
    [SupplyParameterFromQuery(Name = "page")] private int? RequestedPage { get; set; }
    [SupplyParameterFromForm(FormName = "content-comment")]
    private CommentFormInput CommentForm { get; set; } = new();
    private sealed class CommentFormInput
    {
        [Required(ErrorMessage = "請輸入名稱。")]
        [StringLength(Limits.CommentAuthor)]
        public string AuthorName { get; set; } = "";
        [StringLength(Limits.CommentUrl)]
        public string? Url { get; set; }
        public bool Honeypot { get; set; }
        [Required(ErrorMessage = "請輸入評論內容。")]
        [StringLength(Limits.CommentBody)]
        public string Body { get; set; } = "";
    }''', 1)

s = s.replace('''    private string MetaDescription => _content is null ? "" : _content.Excerpt != "" ? _content.Excerpt : Markdown.Excerpt(_content.Markdown);''',
'''    private string MetaDescription => _content is null ? ""
        : _content.MetaDescription != "" ? _content.MetaDescription
        : _content.Excerpt != "" ? _content.Excerpt : Markdown.Excerpt(_content.Markdown);
    /// <summary>schema.org Article 結構化資料；未渲染內容時為空字串。</summary>
    private string StructuredData => _content is null || !_readable ? "" : BuildStructuredData();

    private string BuildStructuredData()
    {
        var data = new System.Text.Json.Nodes.JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = _content!.Kind == ContentKind.Post ? "Article" : "WebPage",
            ["headline"] = _content.Title,
            ["description"] = MetaDescription,
            ["url"] = Canonical,
            ["inLanguage"] = "zh-Hant",
        };
        if (_content.PublishedAt is { } published)
        {
            data["datePublished"] = published.ToString("O");
            data["dateModified"] = (_content.UpdatedAt == default ? published : _content.UpdatedAt).ToString("O");
        }
        if (!string.IsNullOrWhiteSpace(_content.AuthorName))
            data["author"] = new System.Text.Json.Nodes.JsonObject { ["@type"] = "Person", ["name"] = _content.AuthorName };
        if (_firstImage is not null) data["image"] = AbsoluteImage;
        if (_content.Kind == ContentKind.Page) data["isPartOf"] = new System.Text.Json.Nodes.JsonObject { ["@type"] = "WebSite", ["name"] = _settings.Name };
        return data.ToJsonString();
    }''', 1)

s = s.replace('''    private string Canonical => BaseUrl + (_content?.Kind == ContentKind.Page
        ? "/pages/" + string.Join("/", _canonicalSegments.Select(Uri.EscapeDataString))
        : "/posts/" + Uri.EscapeDataString(_content?.Slug ?? ""));''',
'''    private string Canonical => BaseUrl + DetailPath + (_pageIndex > 1 ? "?page=" + _pageIndex : "");
    /// <summary>內容的相對網址；分頁連結與 canonical 都以它為基礎。</summary>
    private string DetailPath => _content?.Kind == ContentKind.Page
        ? "/pages/" + string.Join("/", _canonicalSegments.Select(Uri.EscapeDataString))
        : "/posts/" + Uri.EscapeDataString(_content?.Slug ?? "");
    private string PageHref(int page) => page <= 1 ? DetailPath : DetailPath + "?page=" + page;''', 1)

s = s.replace('''        _html = Markdown.Render(_content.Markdown, _imageWidths);''',
'''        // 長文分頁：頁碼超出範圍與不存在的分頁同義，以 404 回應。
        _pageSections = MarkdownService.SplitPages(_content.Markdown);
        _pageTotal = _pageSections.Count;
        if (RequestedPage is { } requested && (requested < 1 || requested > _pageTotal))
        {
            ServeNotFound();
            return;
        }
        _pageIndex = Math.Clamp(RequestedPage ?? 1, 1, _pageTotal);
        _html = Markdown.Render(_pageSections[_pageIndex - 1], _imageWidths);
        if (_content.Kind == ContentKind.Post && _settings.CommentsEnabled)
        {
            try { _comments = await Api.GetPublicCommentsAsync(_content.Id, ct) ?? []; }
            catch (Exception error) { Logger.LogWarning(error, "評論載入失敗"); }
        }''', 1)

s = s.replace('''    private async Task RenderBodyAsync(CancellationToken ct)''',
'''    private async Task PostCommentAsync()
    {
        if (_content is null || HttpContext is null || HttpContext.Response.HasStarted) return;
        // 線上速率限制：以來源 IP 為鍵，過量提示稍後再試。
        if (!CommentThrottle.TryAcquire(HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            _commentError = "張貼過於頻繁，請稍後再試。";
            return;
        }
        try
        {
            await Api.PostCommentAsync(_content.Id, CommentForm.AuthorName.Trim(), CommentForm.Url?.Trim(), CommentForm.Body.Trim(), CommentForm.Honeypot, HttpContext.RequestAborted);
            _commentPosted = true;
            _commentError = null;
            CommentForm = new CommentFormInput();
            _comments = await Api.GetPublicCommentsAsync(_content.Id, HttpContext.RequestAborted) ?? [];
        }
        catch (CmsException error) { _commentError = error.Message; }
        catch (Exception error)
        {
            Logger.LogWarning(error, "評論送出失敗");
            _commentError = "評論送出失敗，請稍後再試。";
        }
    }

    private async Task RenderBodyAsync(CancellationToken ct)''', 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('contentdetail patched')

p = 'src/Origami.Web/Components/Layout/MainLayout.razor'
s = io.open(p, encoding='utf-8').read()
s = s.replace('''            @foreach (var (page, depth) in NavOrder())
            {
                <a class="@(depth > 0 ? "nav-child" : "")" href="@PageHref(page)">@(depth > 0 ? "└ " : "")@(page.Title)</a>
            }''',
'''            @foreach (var (page, depth) in NavOrder())
            {
                // 自訂導覽項目自帶網址；頁面項目仍依階層網址。
                var href = page.Url ?? PageHref(page);
                <a class="@(depth > 0 ? "nav-child" : "")" href="@href" target="@(page.IsExternal ? "_blank" : null)" rel="@(page.IsExternal ? "noopener" : null)">@(depth > 0 ? "└ " : "")@(page.Title)</a>
            }''', 1)
s = s.replace('''        <a href="/account/login" data-enhance-nav="false">網站管理</a>''',
'''        <a href="/account/login" data-enhance-nav="false">網站管理</a>
        @* 語言切換：以查詢字串帶入並由中介層寫入 cookie，之後的瀏覽沿用該語言。 *@
        <span class="lang-switch">
            <a href="?lang=zh-Hant" hreflang="zh-Hant">中文</a>
            <a href="?lang=en" hreflang="en">English</a>
        </span>''', 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('mainlayout patched')

p = 'src/Origami.Web/Components/Account/Login.razor'
s = io.open(p, encoding='utf-8').read()
s = s.replace('''    <details class="setup-help">''',
'''    <p class="field-help"><a href="/account/forgot-password">忘記密碼？以電子郵件取得重設連結</a></p>
    <details class="setup-help">''', 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('login patched')
