namespace Origami.Tests;

/// <summary>
/// 訪客評論：待審與自動核可、蜜罐與連結過量進垃圾、公開清單只含已核可、
/// 審核與刪除、關閉評論或不可評內容拒絕、匯出匯入只帶已核可評論。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CommentServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private async Task<(Guid Id, string Slug)> CreatePostAsync()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("可評論文章", status: ContentStatus.Published);
        var saved = await admin.Content.SaveAsync(post);
        return (saved.Id!.Value, saved.Slug!);
    }

    private static async Task EnableCommentsAsync(TestActor admin, bool autoApprove = false)
    {
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        settings.CommentAutoApprove = autoApprove;
        await admin.Settings.SaveAsync(settings);
    }

    [PostgresFact]
    public async Task Comments_default_to_pending_and_appear_only_after_approval()
    {
        var (contentId, _) = await CreatePostAsync();
        using var admin = await App.AdministratorAsync();
        await EnableCommentsAsync(admin);
        var comments = admin.Services.GetRequiredService<CommentService>();

        await comments.PostAsync(contentId, new CommentService.CommentPostInput("訪客甲", "https://example.test/", "第一則留言", false));

        // 待審：公開清單看不到，審核佇列看得到。
        Assert.Empty(await comments.ListPublicAsync(contentId));
        var queue = await comments.ListAdminAsync(CommentStatus.Pending, ct: default);
        var pending = Assert.Single(queue.Items);
        Assert.Equal("訪客甲", pending.AuthorName);

        await comments.ModerateAsync(pending.Id, CommentStatus.Approved, pending.Version);
        var published = Assert.Single(await comments.ListPublicAsync(contentId));
        Assert.Equal("第一則留言", published.Markdown);
    }

    [PostgresFact]
    public async Task Auto_approve_honeypot_and_link_flood_are_classified_correctly()
    {
        var (contentId, _) = await CreatePostAsync();
        using var admin = await App.AdministratorAsync();
        await EnableCommentsAsync(admin, autoApprove: true);
        var comments = admin.Services.GetRequiredService<CommentService>();

        // 自動核可：立即公開。
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("訪客乙", null, "自動核可的留言", false));
        Assert.Single(await comments.ListPublicAsync(contentId));

        // 蜜罐命中：靜默進垃圾，不公開也不進待審。
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("機器人", null, "買藥連結", true));
        // 連結過量（> 3 個 http 連結）：同樣進垃圾。
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("連結農場", null,
            "http://a.com http://b.com http://c.com http://d.com 看看這些", false));

        var spam = await comments.ListAdminAsync(CommentStatus.Spam, ct: default);
        Assert.Equal(2, spam.Total);
        Assert.Single(await comments.ListPublicAsync(contentId));
    }

    [PostgresFact]
    public async Task Disabled_comments_and_uncommentable_content_are_refused()
    {
        using var admin = await App.AdministratorAsync();
        var comments = admin.Services.GetRequiredService<CommentService>();
        var (contentId, _) = await CreatePostAsync();

        // 未開放評論。
        await Assert.ThrowsAsync<CmsException>(() => comments.PostAsync(contentId,
            new CommentService.CommentPostInput("訪客", null, "留言", false)));

        // 開放後，草稿與受保護內容仍不可評。
        await EnableCommentsAsync(admin);
        var draft = NewContent("草稿文章");
        var savedDraft = await admin.Content.SaveAsync(draft);
        await Assert.ThrowsAsync<CmsException>(() => comments.PostAsync(savedDraft.Id!.Value,
            new CommentService.CommentPostInput("訪客", null, "留言", false)));

        // 網址協定受限。
        await Assert.ThrowsAsync<CmsException>(() => comments.PostAsync(contentId,
            new CommentService.CommentPostInput("訪客", "javascript:alert(1)", "留言", false)));
    }

    [PostgresTheory]
    [InlineData(ContentStatus.Draft, false, false, ContentKind.Post)]
    [InlineData(ContentStatus.Pending, false, false, ContentKind.Post)]
    [InlineData(ContentStatus.Trashed, false, false, ContentKind.Post)]
    [InlineData(ContentStatus.Published, true, false, ContentKind.Post)]
    [InlineData(ContentStatus.Published, false, true, ContentKind.Post)]
    [InlineData(ContentStatus.Published, false, false, ContentKind.Page)]
    public async Task Approved_comments_recheck_content_visibility_on_every_public_read(
        ContentStatus status, bool isPrivate, bool isProtected, ContentKind kind)
    {
        var (contentId, _) = await CreatePostAsync();
        using var admin = await App.AdministratorAsync();
        await EnableCommentsAsync(admin, autoApprove: true);
        using var visitor = App.Anonymous();
        var comments = visitor.Services.GetRequiredService<CommentService>();
        var input = new CommentService.CommentPostInput("訪客", "https://example.test/", "已核可留言", false);
        await comments.PostAsync(contentId, input);
        var original = Assert.Single(await comments.ListPublicAsync(contentId));
        Assert.Equal(1, await comments.CountApprovedAsync(contentId));

        // 直接改動隔離資料庫以逐一驗證讀取條件，包含服務禁止變更的內容種類。
        await using var db = await App.CreateContextAsync();
        var content = await db.Contents.SingleAsync(x => x.Id == contentId);
        content.Status = status;
        content.IsPrivate = isPrivate;
        content.IsProtected = isProtected;
        content.Kind = kind;
        content.DeletedAt = status == ContentStatus.Trashed ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync();

        Assert.Empty(await comments.ListPublicAsync(contentId));
        Assert.Equal(0, await comments.CountApprovedAsync(contentId));
        await Assert.ThrowsAsync<CmsException>(() => comments.PostAsync(contentId, input));
        // 不可見不代表刪除或取消核可；管理者仍可審核既有留言。
        var queue = await admin.Services.GetRequiredService<CommentService>().ListAdminAsync(CommentStatus.Approved);
        Assert.Equal(original.Id, Assert.Single(queue.Items).Id);

        content.Status = ContentStatus.Published;
        content.IsPrivate = false;
        content.IsProtected = false;
        content.Kind = ContentKind.Post;
        content.DeletedAt = null;
        await db.SaveChangesAsync();

        Assert.Equal(original, Assert.Single(await comments.ListPublicAsync(contentId)));
        Assert.Equal(1, await comments.CountApprovedAsync(contentId));
    }

    [PostgresFact]
    public async Task Approved_comments_follow_current_site_and_its_comment_setting()
    {
        var (firstId, _) = await CreatePostAsync();
        var site = await CreateSiteAsync();
        using var firstAdmin = await App.AdministratorAsync();
        using var secondAdmin = await App.AdministratorAsync(site.Id);
        await EnableCommentsAsync(firstAdmin, autoApprove: true);
        await EnableCommentsAsync(secondAdmin, autoApprove: true);
        var secondPost = await secondAdmin.Content.SaveAsync(NewContent("第二站文章", status: ContentStatus.Published));
        var secondId = secondPost.Id!.Value;
        var firstComments = firstAdmin.Services.GetRequiredService<CommentService>();
        var secondComments = secondAdmin.Services.GetRequiredService<CommentService>();
        await firstComments.PostAsync(firstId, new CommentService.CommentPostInput("第一站訪客", null, "第一站留言", false));
        await secondComments.PostAsync(secondId, new CommentService.CommentPostInput("第二站訪客", null, "第二站留言", false));

        using var visitor = App.Anonymous();
        var comments = visitor.Services.GetRequiredService<CommentService>();
        var first = Assert.Single(await comments.ListPublicAsync(firstId));
        Assert.Equal(1, await comments.CountApprovedAsync(firstId));
        Assert.Empty(await comments.ListPublicAsync(secondId));
        Assert.Equal(0, await comments.CountApprovedAsync(secondId));

        // 同一個服務執行個體切站，不能保留前一站的內容或設定。
        visitor.Site.SiteId = site.Id;
        Assert.Empty(await comments.ListPublicAsync(firstId));
        Assert.Equal(0, await comments.CountApprovedAsync(firstId));
        var second = Assert.Single(await comments.ListPublicAsync(secondId));
        Assert.Equal("第二站留言", second.Markdown);
        Assert.Equal(1, await comments.CountApprovedAsync(secondId));

        var settings = await secondAdmin.Settings.GetAsync();
        settings.CommentsEnabled = false;
        await secondAdmin.Settings.SaveAsync(settings);
        Assert.Empty(await comments.ListPublicAsync(secondId));
        Assert.Equal(0, await comments.CountApprovedAsync(secondId));
        Assert.Equal(second.Id, Assert.Single((await secondComments.ListAdminAsync(CommentStatus.Approved)).Items).Id);
        await Assert.ThrowsAsync<CmsException>(() => comments.PostAsync(secondId,
            new CommentService.CommentPostInput("訪客", null, "關閉後留言", false)));

        visitor.Site.SiteId = SiteDefaults.DefaultSiteId;
        Assert.Equal(first, Assert.Single(await comments.ListPublicAsync(firstId)));
        Assert.Equal(1, await comments.CountApprovedAsync(firstId));
        await EnableCommentsAsync(secondAdmin);
        visitor.Site.SiteId = site.Id;
        Assert.Equal(second, Assert.Single(await comments.ListPublicAsync(secondId)));
        Assert.Equal(1, await comments.CountApprovedAsync(secondId));
        Assert.Empty(await comments.ListPublicAsync(firstId));
        Assert.Equal(0, await comments.CountApprovedAsync(firstId));
    }

    [PostgresTheory]
    [InlineData("http")]
    [InlineData("https")]
    [InlineData("HTTP")]
    [InlineData("HTTPS")]
    [InlineData("HtTpS")]
    public async Task Link_limit_counts_schemes_case_insensitively(string scheme)
    {
        var (contentId, _) = await CreatePostAsync();
        using var admin = await App.AdministratorAsync();
        await EnableCommentsAsync(admin, autoApprove: true);
        var comments = admin.Services.GetRequiredService<CommentService>();
        var links = string.Join(" ", Enumerable.Range(0, Limits.CommentMaxLinks)
            .Select(i => $"{scheme}://example.test/{i}"));
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("正常訪客", null, links, false));
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("過量連結", null,
            links + $" {scheme}://example.test/excess", false));

        Assert.Equal("正常訪客", Assert.Single(await comments.ListPublicAsync(contentId)).AuthorName);
        Assert.Equal(1, await comments.CountApprovedAsync(contentId));
        Assert.Equal("過量連結", Assert.Single((await comments.ListAdminAsync(CommentStatus.Spam)).Items).AuthorName);
    }

    [PostgresFact]
    public async Task Author_urls_preserve_http_and_https_and_reject_unsafe_or_relative_urls()
    {
        var (contentId, _) = await CreatePostAsync();
        using var admin = await App.AdministratorAsync();
        await EnableCommentsAsync(admin, autoApprove: true);
        using var visitor = App.Anonymous();
        var comments = visitor.Services.GetRequiredService<CommentService>();
        var urls = new[] { "http://example.test/path?q=1", "https://example.test/", "HTTP://example.test/", "HtTpS://example.test/" };
        foreach (var url in urls)
            await comments.PostAsync(contentId, new CommentService.CommentPostInput("訪客", " " + url + " ", "留言", false));
        foreach (var url in new[] { "javascript:alert(1)", "data:text/html,test", "file:///test", "ftp://example.test/", "/relative", "//example.test/" })
            await Assert.ThrowsAsync<CmsException>(() => comments.PostAsync(contentId,
                new CommentService.CommentPostInput("訪客", url, "留言", false)));

        var published = await comments.ListPublicAsync(contentId);
        Assert.Equal(urls.Length, published.Count);
        Assert.Equal(urls.Length, await comments.CountApprovedAsync(contentId));
        foreach (var url in urls) Assert.Single(published, comment => comment.Url == url);
    }

    [PostgresFact]
    public async Task Moderation_covers_spam_trash_and_permanent_delete()
    {
        var (contentId, _) = await CreatePostAsync();
        using var admin = await App.AdministratorAsync();
        await EnableCommentsAsync(admin);
        var comments = admin.Services.GetRequiredService<CommentService>();
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("訪客丙", null, "將被移動的留言", false));
        var pending = Assert.Single((await comments.ListAdminAsync(CommentStatus.Pending, ct: default)).Items);

        await comments.ModerateAsync(pending.Id, CommentStatus.Trashed, pending.Version);
        var trashed = await comments.ListAdminAsync(CommentStatus.Trashed, ct: default);
        var trashedItem = Assert.Single(trashed.Items);

        // 每次審核都攜帶當下的版本；沿用舊版本會被拒絕（見 Stale_versions_are_refused）。
        await comments.ModerateAsync(trashedItem.Id, CommentStatus.Pending, trashedItem.Version);
        var reopened = Assert.Single((await comments.ListAdminAsync(CommentStatus.Pending, ct: default)).Items);
        await comments.DeleteAsync(reopened.Id, reopened.Version);
        Assert.Empty((await comments.ListAdminAsync(null, ct: default)).Items);
    }

    [PostgresFact]
    public async Task Stale_versions_are_refused_and_the_newer_change_survives()
    {
        var (contentId, _) = await CreatePostAsync();
        using var admin = await App.AdministratorAsync();
        await EnableCommentsAsync(admin);
        var comments = admin.Services.GetRequiredService<CommentService>();
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("訪客丁", null, "兩分頁同時處理", false));
        var first = Assert.Single((await comments.ListAdminAsync(CommentStatus.Pending, ct: default)).Items);
        var second = first;

        await comments.ModerateAsync(first.Id, CommentStatus.Approved, first.Version);
        // 第二個分頁仍持有核可前的版本：審核與刪除都必須被拒絕，且不覆寫先前的核可。
        await Assert.ThrowsAsync<CmsException>(() =>
            comments.ModerateAsync(second.Id, CommentStatus.Spam, second.Version));
        await Assert.ThrowsAsync<CmsException>(() => comments.DeleteAsync(second.Id, second.Version));
        Assert.Equal("訪客丁", Assert.Single(await comments.ListPublicAsync(contentId)).AuthorName);

        // 重新載入取得最新版本後即可繼續操作。
        var current = Assert.Single((await comments.ListAdminAsync(CommentStatus.Approved, ct: default)).Items);
        Assert.NotEqual(first.Version, current.Version);
        await comments.ModerateAsync(current.Id, CommentStatus.Spam, current.Version);
        Assert.Empty(await comments.ListPublicAsync(contentId));
    }

    [PostgresFact]
    public async Task Export_and_import_carry_approved_comments_only()
    {
        var (contentId, _) = await CreatePostAsync();
        using var admin = await App.AdministratorAsync();
        await EnableCommentsAsync(admin);
        var comments = admin.Services.GetRequiredService<CommentService>();
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("已核可者", null, "公開留言", false));
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("機器人", null, "垃圾", true));
        var pending = Assert.Single((await comments.ListAdminAsync(CommentStatus.Pending, ct: default)).Items);
        await comments.ModerateAsync(pending.Id, CommentStatus.Approved, pending.Version);

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        using var input = new MemoryStream(buffer.ToArray());
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        var restored = await comments.ListAdminAsync(CommentStatus.Approved, ct: default);
        Assert.Single(restored.Items);
        Assert.Equal("已核可者", restored.Items[0].AuthorName);
    }
}
