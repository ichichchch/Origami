namespace Origami.Tests;

/// <summary>
/// P0 / P1 / P2 全量差距補齊整合測試：
/// 1. 完整修訂快照（Slug、分類標籤、自訂詞彙、自訂欄位、SEO、封面、作者）
/// 2. CPT 公開功能閉環（自訂分類法與詞彙彙整、專屬 feed、搜尋結果正確指向 /t/{type}/{slug}）
/// 3. 全站搜尋範圍補齊（includePages 支援頁面與 CPT，DTO 附完整 ResolvedPath）
/// 4. 內容清單進階篩選（作者、分類、標籤、日期區間）與批次編輯（改作者、加分類標籤、評論開關）
/// 5. 隱私權與個資工具（隱私政策頁指定、個人資料匯出、個資抹除與內容匿名化）
/// 6. 管理後台 Site Health（儲存可寫、排程、AI 佇列、Webhook、SMTP、備份、組態，無洩漏秘密）
/// 7. 評論與審稿郵件通知（新評論、提交待審、審核通過／退回通知）
/// 8. OpenAPI v3 規格文件與端點探索
/// 9. Webhook 可靠投遞（資料庫持久化、狀態、重試排程與人工重送）
/// 10. WXR 匯出與附件遷移（含 URL 白名單與 SSRF 防護）
/// 11. Feed 覆蓋率（評論 feed、作者 feed、搜尋 feed、CPT 與自訂分類法 feed）
/// 12. 永久連結（Permalinks）與一般設定（時區、日期／時間格式）
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FullGapClosureTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Full_revision_snapshot_preserves_slug_taxonomies_custom_terms_fields_seo_cover_and_author()
    {
        using var admin = await App.AdministratorAsync();
        var types = admin.Services.GetRequiredService<ContentTypeService>();
        var bookType = await types.SaveAsync(new ContentTypeService.TypeEdit
        {
            Name = "書籍", Slug = "books", SupportsCategories = true, SupportsTags = true
        });
        var genreTax = await types.SaveTaxonomyAsync(new ContentTypeService.TaxonomyEdit
        {
            ContentTypeId = bookType.Id, Name = "領域", Slug = "domain"
        });
        var termA = await types.SaveTermAsync(new ContentTypeService.TermEdit
        {
            TaxonomyId = genreTax.Id, Name = "系統架構", Slug = "arch"
        });
        var isbnField = await types.SaveFieldAsync(new ContentTypeService.FieldEdit
        {
            ContentTypeId = bookType.Id, Name = "ISBN", Key = "isbn", FieldType = CustomFieldTypes.Text
        });
        var catA = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "技術", Slug = "tech" });
        var tagA = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "必讀", Slug = "must-read" });

        await using var db = await App.CreateContextAsync();
        var coverAsset = new MediaAsset
        {
            FileName = "book-cover.png", OriginalName = "book-cover.png", ContentType = "image/png",
            AltText = "初版封面", Caption = "初版圖說", Width = 400, Height = 600, UploadedAt = DateTimeOffset.UtcNow
        };
        db.MediaAssets.Add(coverAsset);
        await db.SaveChangesAsync();

        var v1 = NewContent("第一版書名", status: ContentStatus.Draft);
        v1.Slug = "book-v1";
        v1.Excerpt = "第一版摘要";
        v1.MetaDescription = "第一版 SEO 描述";
        v1.NoIndex = true;
        v1.AllowComments = false;
        v1.CoverMediaId = coverAsset.Id;
        v1.ContentTypeId = bookType.Id;
        v1.CategoryIds = [catA.Id!.Value];
        v1.TagIds = [tagA.Id!.Value];
        v1.CustomTermIds = [termA.Id];
        v1.FieldValues = [new FieldValueInput(isbnField.Id, "978-986-000001")];
        var savedV1 = await admin.Content.SaveAsync(v1);

        // 修改所有欄位產生修訂版。
        var v2 = await admin.Content.GetEditAsync(savedV1.Id!.Value);
        v2.Title = "第二版書名";
        v2.Slug = "book-v2";
        v2.Markdown = "第二版正文";
        v2.Excerpt = "第二版摘要";
        v2.MetaDescription = "第二版 SEO";
        v2.NoIndex = false;
        v2.AllowComments = true;
        v2.CoverMediaId = null;
        v2.CategoryIds = [];
        v2.TagIds = [];
        v2.CustomTermIds = [];
        v2.FieldValues = [new FieldValueInput(isbnField.Id, "978-986-000002")];
        await admin.Content.UpdateAsync(v2);

        var revisions = await admin.Content.ListRevisionsAsync(savedV1.Id.Value);
        var revItem = Assert.Single(revisions);
        var detail = await admin.Content.GetRevisionAsync(revItem.Id);
        Assert.NotNull(detail);
        Assert.Equal("第一版書名", detail!.Title);
        Assert.Equal("book-v1", detail.Slug);
        Assert.Equal("第一版摘要", detail.Excerpt);
        Assert.Equal("第一版 SEO 描述", detail.MetaDescription);
        Assert.True(detail.NoIndex);
        Assert.False(detail.AllowComments);
        Assert.Equal(coverAsset.Id, detail.CoverMediaId);
        Assert.Equal("book-cover.png", detail.CoverFileName);
        Assert.Equal("初版封面", detail.CoverAltText);
        Assert.Equal([catA.Id.Value], detail.CategoryIds);
        Assert.Equal([tagA.Id.Value], detail.TagIds);
        Assert.Equal([termA.Id], detail.CustomTermIds);
        Assert.Equal("978-986-000001", Assert.Single(detail.FieldValues).Value);
    }

    [PostgresFact]
    public async Task Cpt_custom_taxonomy_archive_feed_and_type_aware_search_work()
    {
        using var admin = await App.AdministratorAsync();
        var types = admin.Services.GetRequiredService<ContentTypeService>();
        var movieType = await types.SaveAsync(new ContentTypeService.TypeEdit { Name = "影評", Slug = "movies" });
        var genreTax = await types.SaveTaxonomyAsync(new ContentTypeService.TaxonomyEdit
        {
            ContentTypeId = movieType.Id, Name = "類型", Slug = "genre", IsHierarchical = true
        });
        var scifiTerm = await types.SaveTermAsync(new ContentTypeService.TermEdit
        {
            TaxonomyId = genreTax.Id, Name = "科幻", Slug = "sci-fi", Description = "硬核與太空科幻"
        });

        var movie = NewContent("星際效應深度解析", status: ContentStatus.Published);
        movie.Slug = "interstellar-review";
        movie.Markdown = "探討黑洞與時間膨脹的經典電影 SUPER-KEY-777。";
        movie.ContentTypeId = movieType.Id;
        movie.CustomTermIds = [scifiTerm.Id];
        await admin.Content.SaveAsync(movie);

        var page = NewContent("關於我們團隊", kind: ContentKind.Page, status: ContentStatus.Published);
        page.Slug = "about-team";
        page.Markdown = "我們是一群熱愛寫作的工程師 SUPER-KEY-777。";
        await admin.Content.SaveAsync(page);

        // 1. 自訂分類法公開詞彙查詢與篩選列表
        var termArchive = await admin.Content.GetPublicCustomTermAsync("movies", "genre", "sci-fi");
        Assert.NotNull(termArchive);
        Assert.Equal("科幻", termArchive!.TermName);

        var byTerm = await admin.Content.ListByTypeAsync("movies", taxonomySlug: "genre", termSlug: "sci-fi");
        Assert.NotNull(byTerm);
        Assert.Single(byTerm!.Items);
        Assert.Equal("interstellar-review", byTerm.Items[0].Slug);

        var detail = await admin.Content.GetPublishedByTypeAsync("movies", "interstellar-review");
        Assert.NotNull(detail);
        Assert.Contains(detail!.CustomTerms, t => t.TaxonomySlug == "genre" && t.TermSlug == "sci-fi");

        // 2. CPT 與自訂分類法專屬 Feed
        var cptFeed = await admin.Content.FeedAsync(10, typeSlug: "movies", customTaxonomy: "genre", customTerm: "sci-fi");
        var feedItem = Assert.Single(cptFeed);
        Assert.Equal("/t/movies/interstellar-review", feedItem.PublicPath);

        // 3. Type-aware 搜尋：CPT 正確連向 /t/movies/interstellar-review，且 includePages=true 涵蓋獨立頁面
        var search = admin.Services.GetRequiredService<PublicSearchService>();
        var postAndCptHits = await search.SearchAsync("SUPER-KEY-777", includePages: false);
        var cptHit = Assert.Single(postAndCptHits.Hits);
        Assert.Equal("movies", cptHit.ContentTypeSlug);
        Assert.Equal("/t/movies/interstellar-review", cptHit.ResolvedPath);

        var allHits = await search.SearchAsync("SUPER-KEY-777", includePages: true);
        Assert.Equal(2, allHits.Hits.Count);
        Assert.Contains(allHits.Hits, h => h.Kind == ContentKind.Page && h.ResolvedPath == "/pages/about-team");
    }

    [PostgresFact]
    public async Task Advanced_admin_content_filters_and_bulk_editing_work()
    {
        using var admin = await App.AdministratorAsync();
        var writerUser = await CreateMemberAsync(SiteRole.Author, name: "writer1");
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = "writer1", Role = SiteRole.Author, DisplayName = "專欄作家",
            Version = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == writerUser.Id).Version
        });

        var cat1 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "隨筆", Slug = "essay" });
        var tag1 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "精選", Slug = "featured" });

        var p1 = await admin.Content.SaveAsync(NewContent("文章一", status: ContentStatus.Published));
        var p2 = await admin.Content.SaveAsync(NewContent("文章二", status: ContentStatus.Draft));

        // 批次改派作者、批次加分類、批次加標籤、批次關閉評論
        var items = (await admin.Content.ListAdminAsync(ContentKind.Post)).Items;
        var batch = items.Select(x => (x.Id, x.Version)).ToList();
        var r1 = await admin.Content.BulkAsync(BulkAction.SetAuthor, batch, options: new BulkEditOptions(AuthorId: writerUser.Id));
        Assert.All(r1, x => Assert.True(x.Succeeded));

        batch = (await admin.Content.ListAdminAsync(ContentKind.Post)).Items.Select(x => (x.Id, x.Version)).ToList();
        var r2 = await admin.Content.BulkAsync(BulkAction.AddCategory, batch, options: new BulkEditOptions(CategoryId: cat1.Id));
        Assert.All(r2, x => Assert.True(x.Succeeded));

        batch = (await admin.Content.ListAdminAsync(ContentKind.Post)).Items.Select(x => (x.Id, x.Version)).ToList();
        var r3 = await admin.Content.BulkAsync(BulkAction.AddTag, batch, options: new BulkEditOptions(TagId: tag1.Id));
        Assert.All(r3, x => Assert.True(x.Succeeded));

        batch = (await admin.Content.ListAdminAsync(ContentKind.Post)).Items.Select(x => (x.Id, x.Version)).ToList();
        var r4 = await admin.Content.BulkAsync(BulkAction.DisableComments, batch);
        Assert.All(r4, x => Assert.True(x.Succeeded));

        // 驗證進階篩選（依作者、分類、標籤與日期區間）
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var filtered = await admin.Content.ListAdminAsync(
            ContentKind.Post,
            authorId: writerUser.Id,
            categoryId: cat1.Id,
            tagId: tag1.Id,
            dateFrom: today.AddDays(-1),
            dateTo: today.AddDays(1));
        Assert.Equal(2, filtered.Total);
        Assert.All(filtered.Items, x =>
        {
            Assert.Equal("專欄作家", x.AuthorName);
            Assert.False(x.AllowComments);
        });
    }

    [PostgresFact]
    public async Task Privacy_policy_page_personal_data_export_and_erasure_workflow_work()
    {
        using var admin = await App.AdministratorAsync();
        var privacyPage = await admin.Content.SaveAsync(NewContent("隱私權政策", kind: ContentKind.Page, status: ContentStatus.Published));
        var settings = await admin.Settings.GetAsync();
        settings.PrivacyPageContentId = privacyPage.Id;
        settings.PermalinkScheme = PermalinkRules.MonthAndName;
        settings.TimeZoneId = "UTC";
        settings.DateFormat = "yyyy-MM-dd";
        settings.TimeFormat = "HH:mm";
        await admin.Settings.SaveAsync(settings);

        var reloadedSettings = await admin.Settings.GetAsync();
        Assert.Equal(privacyPage.Id, reloadedSettings.PrivacyPageContentId);
        Assert.Equal(PermalinkRules.MonthAndName, reloadedSettings.PermalinkScheme);

        var authorUser = await admin.Sites.CreateUserAsync(new UserCreateInput
        {
            UserName = "gdpr-user", Password = IsolatedPostgres.NewPassword(), Email = "gdpr@example.test"
        });
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = "gdpr-user", Role = SiteRole.Author, DisplayName = "張三"
        });

        using (var authorActor = await App.UserAsync(authorUser.Id))
        {
            await authorActor.Content.SaveAsync(NewContent("張三的草稿", status: ContentStatus.Draft));
            var export = await authorActor.Sites.ExportMyPersonalDataAsync();
            Assert.Equal("gdpr-user", export.UserName);
            Assert.Equal("gdpr@example.test", export.Email);
            Assert.Single(export.Memberships);
            Assert.Single(export.AuthoredContents);
        }

        var eraseResult = await admin.Sites.EraseUserPersonalDataAsync(authorUser.Id);
        Assert.Equal(1, eraseResult.AnonymizedContents);
        Assert.Equal(1, eraseResult.RemovedMemberships);

        var afterContents = await admin.Content.ListAdminAsync(ContentKind.Post);
        var anonPost = Assert.Single(afterContents.Items);
        Assert.Null(anonPost.AuthorId);
        Assert.Equal("", anonPost.AuthorName);
    }

    [PostgresFact]
    public async Task Email_notifications_fire_for_comments_pending_submissions_and_review_outcomes()
    {
        using var admin = await App.AdministratorAsync();
        var sender = admin.Services.GetRequiredService<TestEmailSender>();

        var editorUser = await admin.Sites.CreateUserAsync(new UserCreateInput
        {
            UserName = "chief-editor", Password = IsolatedPostgres.NewPassword(), Email = "editor@example.test"
        });
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = "chief-editor", Role = SiteRole.Editor, DisplayName = "總編"
        });

        var authorUser = await admin.Sites.CreateUserAsync(new UserCreateInput
        {
            UserName = "contrib-author", Password = IsolatedPostgres.NewPassword(), Email = "author@example.test"
        });
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = "contrib-author", Role = SiteRole.Author, DisplayName = "投稿作者"
        });

        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        settings.CommentAutoApprove = false;
        settings.EmailNotificationsEnabled = true;
        await admin.Settings.SaveAsync(settings);

        // 1. 作者提交待審 -> 通知編輯者
        Guid pendingPostId;
        using (var authorActor = await App.UserAsync(authorUser.Id))
        {
            var draft = NewContent("待審專文", status: ContentStatus.Pending);
            var saved = await authorActor.Content.SaveAsync(draft);
            pendingPostId = saved.Id!.Value;
        }
        Assert.Contains(sender.Messages, m => m.To == "editor@example.test" && m.Subject.Contains("新待審內容"));

        // 2. 編輯者審核通過並發布 -> 通知原作者
        using (var editorActor = await App.UserAsync(editorUser.Id))
        {
            var edit = await editorActor.Content.GetEditAsync(pendingPostId);
            edit.Status = ContentStatus.Published;
            await editorActor.Content.UpdateAsync(edit);
        }
        Assert.Contains(sender.Messages, m => m.To == "author@example.test" && m.Subject.Contains("審稿結果通知"));

        // 3. 訪客張貼新評論 -> 通知文章作者與編輯者
        var comments = admin.Services.GetRequiredService<CommentService>();
        await comments.PostAsync(pendingPostId, new CommentService.CommentPostInput("熱心讀者", null, "寫得太棒了！", false));
        Assert.Contains(sender.Messages, m => m.To == "author@example.test" && m.Subject.Contains("新評論通知"));
        Assert.Contains(sender.Messages, m => m.To == "editor@example.test" && m.Subject.Contains("新評論通知"));
    }

    [PostgresFact]
    public async Task Site_health_persistent_webhooks_wxr_export_and_feed_coverage_work()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        settings.CommentAutoApprove = true;
        settings.WebhookUrl = "https://example.com/hooks/test";
        settings.WebhookSecret = "secret-key-123";
        await admin.Settings.SaveAsync(settings);

        var post = NewContent("全功能測試文章", status: ContentStatus.Published);
        post.Slug = "full-feature-post";
        post.Markdown = "這是一篇用於驗證 WXR 匯出與 Feed 的文章。";
        var savedPost = await admin.Content.SaveAsync(post);

        var comments = admin.Services.GetRequiredService<CommentService>();
        await comments.PostAsync(savedPost.Id!.Value, new CommentService.CommentPostInput("評論者甲", "https://example.org", "精采留言內容", false));

        // 1. 驗證持久化 Webhook 紀錄與手動重送
        var webhooks = admin.Services.GetRequiredService<WebhookService>();
        var deliveries = await webhooks.ListAdminAsync();
        Assert.True(deliveries.Total >= 2);
        var retried = await webhooks.RetryManualAsync(deliveries.Items[0].Id);
        Assert.True(retried.Attempts >= 2);

        // 2. 驗證 Site Health 報告不洩漏秘密且包含完整檢查項目
        var healthService = admin.Services.GetRequiredService<SiteHealthService>();
        var report = await healthService.GetReportAsync();
        Assert.Contains(report.Items, x => x.Key == "database" && x.Status == "good");
        Assert.Contains(report.Items, x => x.Key == "storage" && x.Status == "good");
        Assert.Contains(report.Items, x => x.Key == "webhooks");
        Assert.All(report.Items, x => Assert.DoesNotContain("secret-key-123", x.Summary));

        // 3. 驗證評論 Feed、搜尋 Feed
        var commentFeed = await comments.FeedCommentsAsync(10);
        Assert.Single(commentFeed);
        Assert.Equal("評論者甲", commentFeed[0].AuthorName);

        var searchFeed = await admin.Content.FeedAsync(10, search: "WXR 匯出");
        Assert.Single(searchFeed);
        Assert.Equal("full-feature-post", searchFeed[0].Slug);

        // 4. 驗證 WXR 匯出與 SSRF 防護
        var wxr = admin.Services.GetRequiredService<WxrImportService>();
        using var wxrStream = new MemoryStream();
        await wxr.ExportAsync(wxrStream);
        var wxrXml = Encoding.UTF8.GetString(wxrStream.ToArray());
        Assert.Contains("<wp:wxr_version>1.2</wp:wxr_version>", wxrXml);
        Assert.Contains("full-feature-post", wxrXml);
        Assert.Contains("精采留言內容", wxrXml);

        Assert.False(WxrImportService.IsSafeAttachmentUrl("http://127.0.0.1/secret.png", new HashSet<string>(), out _, out _));
        Assert.False(WxrImportService.IsSafeAttachmentUrl("https://evil.example.com/pic.png", new HashSet<string>(["trusted.example.com"]), out _, out _));
        Assert.True(WxrImportService.IsSafeAttachmentUrl("https://trusted.example.com/uploads/pic.png", new HashSet<string>(["trusted.example.com"]), out _, out _));
    }
}
