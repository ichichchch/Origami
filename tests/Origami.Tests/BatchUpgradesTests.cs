namespace Origami.Tests;

/// <summary>
/// 推薦批次功能測試：
/// 1. 後台總覽儀表板與 7/30 日瀏覽趨勢
/// 2. 評論樓中樓（ParentId）、管理員直接回覆、單篇內容獨立評論開關
/// 3. 分類父子階層與頁面排序權重（MenuOrder）
/// 4. 作者個人資料（Bio／網站／頭像）與自助修改密碼
/// 5. 編輯器擴充語法（[toc] 目錄、::: note 提示框、腳註）
/// 6. 301 重定向管理
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BatchUpgradesTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [Fact]
    public void Markdown_supports_toc_callouts_and_footnotes()
    {
        var md = new MarkdownService();
        var source = """
            [toc]

            ## 第一節
            正文內容[^1]。

            ### 子節

            ::: note
            這是一段重要提示。
            :::

            [^1]: 這是腳註說明。
            """;
        var html = md.Render(source);
        Assert.Contains("<nav class=\"toc\"", html);
        Assert.Contains("第一節", html);
        Assert.Contains("子節", html);
        Assert.Contains("class=\"note\"", html);
        Assert.Contains("這是一段重要提示。", html);
        Assert.Contains("這是腳註說明。", html);
    }

    [PostgresFact]
    public async Task Hierarchical_categories_aggregate_child_posts_and_prevent_cycles()
    {
        using var admin = await App.AdministratorAsync();
        var parent = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "技術", Slug = "tech" });
        var child = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit
        {
            Name = "後端", Slug = "backend", ParentId = parent.Id
        });

        // 不可將父分類設為其子分類的下層（循環保護）。
        await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit
        {
            Id = parent.Id, Name = parent.Name, Slug = parent.Slug, ParentId = child.Id, Version = parent.Version
        }));

        // 文章僅指派給子分類時，篩選父分類也能列出該文章。
        var post = NewContent("DotNet 實戰", status: ContentStatus.Published);
        post.CategoryIds = [child.Id!.Value];
        await admin.Content.SaveAsync(post);

        var listByParent = await admin.Content.ListPublishedAsync(category: "tech");
        Assert.Single(listByParent.Items);

        var parentView = await admin.Content.GetPublicTaxonomyAsync(TaxonomyKind.Category, "tech");
        Assert.NotNull(parentView);

        var childView = await admin.Content.GetPublicTaxonomyAsync(TaxonomyKind.Category, "backend");
        Assert.NotNull(childView);
        Assert.Equal("tech", childView.ParentSlug);
        Assert.Equal("技術", childView.ParentTitle);
    }

    [PostgresFact]
    public async Task Page_menu_order_controls_default_navigation_and_admin_list_order()
    {
        using var admin = await App.AdministratorAsync();
        var pageB = NewContent("關於我們", kind: ContentKind.Page, status: ContentStatus.Published);
        pageB.MenuOrder = 20;
        await admin.Content.SaveAsync(pageB);

        var pageA = NewContent("服務介紹", kind: ContentKind.Page, status: ContentStatus.Published);
        pageA.MenuOrder = 5;
        await admin.Content.SaveAsync(pageA);

        var nav = await admin.Services.GetRequiredService<NavigationService>().PublicAsync();
        Assert.Equal(["服務介紹", "關於我們"], nav.Select(x => x.Title).ToArray());

        var adminPages = await admin.Content.ListAdminAsync(ContentKind.Page);
        Assert.Equal(["服務介紹", "關於我們"], adminPages.Items.Select(x => x.Title).ToArray());
    }

    [PostgresFact]
    public async Task Per_post_comment_toggle_and_threaded_admin_replies_work()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        settings.CommentAutoApprove = false;
        await admin.Settings.SaveAsync(settings);

        var closedPost = NewContent("關閉評論的文章", status: ContentStatus.Published);
        closedPost.AllowComments = false;
        var savedClosed = await admin.Content.SaveAsync(closedPost);

        var comments = admin.Services.GetRequiredService<CommentService>();
        await Assert.ThrowsAsync<CmsException>(() => comments.PostAsync(savedClosed.Id!.Value,
            new CommentService.CommentPostInput("訪客", null, "嘗試留言", false)));

        var openPost = NewContent("開放評論的文章", status: ContentStatus.Published);
        openPost.AllowComments = true;
        var savedOpen = await admin.Content.SaveAsync(openPost);

        await comments.PostAsync(savedOpen.Id!.Value, new CommentService.CommentPostInput("張三", null, "請問這個功能怎麼用？", false));
        var pending = Assert.Single((await comments.ListAdminAsync(CommentStatus.Pending)).Items);

        // 管理員直接回覆待審評論時，原評論自動核可，且回覆帶官方標記與父評論關聯。
        var reply = await comments.ReplyAdminAsync(pending.Id, "感謝提問，請參考說明文件。");
        Assert.True(reply.IsAdminReply);
        Assert.Equal(pending.Id, reply.ParentId);
        Assert.Equal("張三", reply.ParentAuthorName);

        var publicList = await comments.ListPublicAsync(savedOpen.Id.Value);
        Assert.Equal(2, publicList.Count);
        Assert.Contains(publicList, x => x.IsAdminReply && x.ParentId == pending.Id);
    }

    [PostgresFact]
    public async Task Self_service_profile_and_password_change_update_author_archive()
    {
        var initialPassword = IsolatedPostgres.NewPassword();
        using var admin = await App.AdministratorAsync();
        var user = await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = "writer1", Password = initialPassword });
        await admin.Sites.SetMemberAsync(new MemberEditInput { UserName = "writer1", Role = SiteRole.Editor });

        using var actor = await App.UserAsync(user.Id);
        var post = await actor.Content.SaveAsync(NewContent("專欄文章", status: ContentStatus.Published));

        var updated = await actor.Sites.UpdateMyProfileAsync(new AccountProfileEdit
        {
            DisplayName = "林語堂",
            Bio = "專注於現代散文與技術寫作。",
            WebsiteUrl = "https://example.com",
            AvatarUrl = "/media/avatar123.png",
            Email = "writer1@example.com"
        });
        Assert.Equal("林語堂", updated.DisplayName);

        var publicAuthor = await actor.Content.GetPublicAuthorAsync("林語堂");
        Assert.NotNull(publicAuthor);
        Assert.Equal("專注於現代散文與技術寫作。", publicAuthor.Bio);
        Assert.Equal("https://example.com", publicAuthor.WebsiteUrl);
        Assert.Equal("/media/avatar123.png", publicAuthor.AvatarUrl);

        await actor.Sites.ChangeMyPasswordAsync(new ChangePasswordInput
        {
            CurrentPassword = initialPassword,
            NewPassword = "NewStrong#Pass99"
        });
    }

    [PostgresFact]
    public async Task Redirects_manager_and_dashboard_summary_report_accurate_data()
    {
        using var admin = await App.AdministratorAsync();
        var post = await admin.Content.SaveAsync(NewContent("重定向目標文章", status: ContentStatus.Published));
        await admin.Content.RecordViewAsync(post.Id!.Value);
        await admin.Content.RecordViewAsync(post.Id.Value);

        var redirect = await admin.Content.CreateRedirectAsync(new RedirectCreateInput("legacy-url-slug", post.Id.Value));
        Assert.Equal("legacy-url-slug", redirect.OldSlug);

        var resolved = await admin.Content.RedirectTargetAsync("legacy-url-slug");
        Assert.NotNull(resolved);
        Assert.Equal(post.Slug, resolved.Slug);

        var dashboard = await admin.Content.GetDashboardAsync();
        Assert.Equal(1, dashboard.Posts.Published);
        Assert.Equal(1, dashboard.RedirectCount);
        Assert.Equal(2, dashboard.TotalViews7Days);
        Assert.Equal(2, dashboard.TotalViews30Days);
        Assert.Single(dashboard.TopContents);

        await admin.Content.DeleteRedirectAsync(redirect.Id);
        Assert.Empty(await admin.Content.ListRedirectsAsync());
    }
}
