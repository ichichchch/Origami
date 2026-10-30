namespace Origami.Tests;

/// <summary>
/// 第一、第二、第三優先級批次（1-2-3）整合與單元測試：
/// - 1.1~1.3：可摺疊區塊（::: details）、原生音訊／影片／PDF 卡片、字數與閱讀時長統計
/// - 1.4 & 1.7：一鍵複製／克隆文章為草稿、Editor/Admin 修改文章署名作者
/// - 3.1：媒體庫按種類（MediaKind）篩選
/// - 2.1, 2.4, 5.5：頁尾版權與社交連結、深淺色模式、層級頁面麵包屑
/// - 4.2 & 6.1：評論敏感詞黑名單過濾、舊文章超過 N 天自動關閉評論、站點維護模式
/// - 6.2 & 6.3：操作審計日誌（AuditLog）、2FA 一次性備用恢復碼與全站管理者重設成員 2FA
/// - 5.2, 5.3, 6.4：自訂欄位擴充（URL／Select／Media）、出站 Webhooks（HMAC 簽章）、編輯器 AI 摘要與 SEO 描述建議
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PriorityBatches123Tests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [Fact]
    public void Markdown_supports_details_blocks_media_cards_and_reading_time()
    {
        var md = new MarkdownService();
        var source = """
            ::: details 點擊展開常見問題
            這裡是摺疊起來的補充解答。
            :::

            [範例音訊](/media/0123456789abcdef0123456789abcdef.mp3)

            [範例影片](/media/fedcba9876543210fedcba9876543210.mp4)

            [技術白皮書 PDF](/media/11112222333344445555666677778888.pdf)
            """;

        var html = md.Render(source);
        Assert.Contains("<details class=\"details-block\"><summary>點擊展開常見問題</summary>", html);
        Assert.Contains("這裡是摺疊起來的補充解答。", html);
        Assert.Contains("<audio controls preload=\"metadata\" src=\"/media/0123456789abcdef0123456789abcdef.mp3\">", html);
        Assert.Contains("<video controls preload=\"metadata\" src=\"/media/fedcba9876543210fedcba9876543210.mp4\">", html);
        Assert.Contains("class=\"file-download-link\" href=\"/media/11112222333344445555666677778888.pdf\"", html);

        var (words, minutes) = md.WordCountAndReadingTime("這是一段測試文字 hello world 共有幾個詞。");
        Assert.True(words >= 12);
        Assert.Equal(1, minutes);
    }

    [PostgresFact]
    public async Task Duplicate_content_and_reassign_author_work_and_record_audit_logs()
    {
        var password = IsolatedPostgres.NewPassword();
        using var admin = await App.AdministratorAsync();
        var authorUser = await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = "coauthor", Password = password });
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = "coauthor",
            Role = SiteRole.Author,
            DisplayName = "王小明"
        });

        var cat = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "專欄", Slug = "column" });
        var original = NewContent("原始深度文章", status: ContentStatus.Published);
        original.CategoryIds = [cat.Id!.Value];
        original.MetaDescription = "原始 SEO 描述";
        var saved = await admin.Content.SaveAsync(original);

        // 複製為新草稿，保留分類與 SEO 設定。
        var duplicated = await admin.Content.DuplicateAsync(saved.Id!.Value);
        Assert.NotNull(duplicated.Id);
        Assert.NotEqual(saved.Id, duplicated.Id);
        Assert.Equal(ContentStatus.Draft, duplicated.Status);
        Assert.Contains("原始深度文章 (副本)", duplicated.Title);
        Assert.Equal([cat.Id.Value], duplicated.CategoryIds);
        Assert.Equal("原始 SEO 描述", duplicated.MetaDescription);

        // 編輯者／管理員可改派文章署名作者。
        var options = await admin.Content.AuthorOptionsAsync();
        Assert.Contains(options, x => x.UserId == authorUser.Id && x.DisplayName == "王小明");

        saved.AuthorId = authorUser.Id;
        var reassigned = await admin.Content.SaveAsync(saved);
        Assert.Equal(authorUser.Id, reassigned.AuthorId);
        Assert.Equal("王小明", reassigned.AuthorName);

        // 審計日誌應記錄建立與發布操作。
        var audits = await admin.Content.ListAuditLogsAsync();
        Assert.NotEmpty(audits.Items);
        Assert.Contains(audits.Items, x => x.Action == "content.published");
    }

    [PostgresFact]
    public async Task Media_kind_filter_and_hierarchical_page_breadcrumbs_work()
    {
        using var admin = await App.AdministratorAsync();
        using var pdfStream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4\n%test pdf content"));
        var uploadedPdf = await admin.Media.UploadFileAsync(pdfStream, "manual.pdf");
        Assert.Equal(MediaKind.Document, uploadedPdf.Kind);

        var docsOnly = await admin.Media.ListAsync(kind: MediaKind.Document);
        Assert.Single(docsOnly.Items);
        Assert.Equal("manual.pdf", docsOnly.Items[0].OriginalName);

        var audioOnly = await admin.Media.ListAsync(kind: MediaKind.Audio);
        Assert.Empty(audioOnly.Items);

        // 層級頁面解析同時回傳完整麵包屑節點。
        var parentPage = await admin.Content.SaveAsync(NewContent("產品中心", kind: ContentKind.Page, status: ContentStatus.Published));
        var childInput = NewContent("企業版方案", kind: ContentKind.Page, status: ContentStatus.Published);
        childInput.ParentId = parentPage.Id;
        var childPage = await admin.Content.SaveAsync(childInput);

        var resolved = await admin.Content.ResolvePublishedPagePathAsync([parentPage.Slug!, childPage.Slug!]);
        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.Breadcrumbs.Count);
        Assert.Equal("產品中心", resolved.Breadcrumbs[0].Title);
        Assert.Equal("/pages/" + Uri.EscapeDataString(parentPage.Slug!), resolved.Breadcrumbs[0].Path);
        Assert.Equal("企業版方案", resolved.Breadcrumbs[1].Title);
    }

    [PostgresFact]
    public async Task Comment_blocked_words_auto_close_days_maintenance_and_webhooks_work()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        settings.CommentAutoApprove = true;
        settings.CommentBlockedWords = "賭博, 免費領取, casino";
        settings.CommentCloseDays = 14;
        settings.ColorScheme = ThemePresets.ColorSchemeDark;
        settings.FooterText = "© 2026 Origami Publishing";
        settings.SocialGithubUrl = "https://github.com/origami";
        settings.MaintenanceMode = true;
        settings.MaintenanceMessage = "系統升級維護中";
        settings.WebhookUrl = "https://example.com/hooks/origami";
        settings.WebhookSecret = "top-secret-hmac-key";
        await admin.Settings.SaveAsync(settings);

        var post = await admin.Content.SaveAsync(NewContent("Webhook 測試文章", status: ContentStatus.Published));
        var webhooks = admin.Services.GetRequiredService<WebhookService>();
        Assert.Contains(webhooks.RecentDeliveries, x => x.Event == "content.published" && x.Signature != null && x.Signature.StartsWith("sha256="));

        // 命中敏感詞黑名單的評論自動標為 Spam。
        var comments = admin.Services.GetRequiredService<CommentService>();
        await comments.PostAsync(post.Id!.Value, new CommentService.CommentPostInput("路人", null, "快來免費領取大獎！", false));
        var spamList = await comments.ListAdminAsync(CommentStatus.Spam);
        Assert.Single(spamList.Items);

        // 正常評論自動核可並觸發 comment.created webhook。
        await comments.PostAsync(post.Id.Value, new CommentService.CommentPostInput("讀者", null, "非常有幫助的文章！", false));
        Assert.Contains(webhooks.RecentDeliveries, x => x.Event == "comment.created");

        // 超過自動關閉天數的舊文章拒絕新評論。
        await using (var db = await App.CreateContextAsync())
        {
            var oldDate = DateTimeOffset.UtcNow.AddDays(-20);
            await db.Contents.Where(x => x.Id == post.Id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PublishedAt, oldDate));
        }
        var ex = await Assert.ThrowsAsync<CmsException>(() =>
            comments.PostAsync(post.Id.Value, new CommentService.CommentPostInput("讀者2", null, "遲來的留言", false)));
        Assert.Contains("超過評論開放期限", ex.Message);
    }

    [PostgresFact]
    public async Task Custom_fields_url_select_media_and_ai_summary_suggestion_work()
    {
        using var admin = await App.AdministratorAsync();
        var types = admin.Services.GetRequiredService<ContentTypeService>();
        var bookType = await types.SaveAsync(new ContentTypeService.TypeEdit
        {
            Name = "書籍評介", Slug = "books"
        });

        var levelField = await types.SaveFieldAsync(new ContentTypeService.FieldEdit
        {
            ContentTypeId = bookType.Id,
            Name = "難度分級",
            Key = "difficulty",
            FieldType = CustomFieldTypes.Select,
            Options = "入門, 進階, 專家",
            IsRequired = true
        });
        var linkField = await types.SaveFieldAsync(new ContentTypeService.FieldEdit
        {
            ContentTypeId = bookType.Id,
            Name = "購書連結",
            Key = "buy-url",
            FieldType = CustomFieldTypes.Url
        });
        var sampleMediaField = await types.SaveFieldAsync(new ContentTypeService.FieldEdit
        {
            ContentTypeId = bookType.Id,
            Name = "試讀 PDF",
            Key = "sample-pdf",
            FieldType = CustomFieldTypes.Media
        });

        // 不在選項內的 select 值應被拒絕。
        var item = NewContent("C# 深入解析", status: ContentStatus.Published);
        item.ContentTypeId = bookType.Id;
        item.FieldValues = [new FieldValueInput(levelField.Id, "不存在的等級")];
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(item));

        // 合法的 select / url / media 欄位值可正常儲存與公開讀取。
        item.FieldValues =
        [
            new FieldValueInput(levelField.Id, "進階"),
            new FieldValueInput(linkField.Id, "https://example.com/buy"),
            new FieldValueInput(sampleMediaField.Id, "/media/0123456789abcdef0123456789abcdef.pdf")
        ];
        await admin.Content.SaveAsync(item);

        var published = await admin.Content.GetPublishedByTypeAsync("books", item.Slug);
        Assert.NotNull(published);
        Assert.Equal(3, published.Fields.Count);
        Assert.Contains(published.Fields, f => f.Key == "difficulty" && f.Value == "進階");

        // AI 摘要與 SEO 建議（即使未設定遠端模型也能透過 fallback 安全回傳）。
        var assistant = admin.Services.GetRequiredService<SiteAssistant>();
        var md = admin.Services.GetRequiredService<MarkdownService>();
        var suggestion = await assistant.SuggestSummaryAsync("C# 13 新特性總覽", "本文詳細介紹 .NET 10 與 C# 的最新語法與效能改進。", md);
        Assert.NotEmpty(suggestion.Excerpt);
        Assert.NotEmpty(suggestion.MetaDescription);
    }

    [PostgresFact]
    public async Task Two_factor_recovery_codes_and_network_admin_2fa_reset_work()
    {
        var password = IsolatedPostgres.NewPassword();
        using var admin = await App.AdministratorAsync();
        var created = await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = "editor2fa", Password = password });
        await admin.Sites.SetMemberAsync(new MemberEditInput { UserName = "editor2fa", Role = SiteRole.Editor });

        using var userActor = await App.UserAsync(created.Id);
        var twoFactor = userActor.Services.GetRequiredService<TwoFactorService>();
        var (secret, _) = await twoFactor.BeginSetupAsync();
        var validTotp = Rfc6238Code(Base32Decode(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var recoveryCodes = await twoFactor.EnableAsync(validTotp);
        Assert.Equal(Limits.RecoveryCodesCount, recoveryCodes.Count);

        // EnableAsync 輪替了安全戳記，重新取得 Actor 以取得最新 Principal。
        using var refreshedActor = await App.UserAsync(created.Id);
        var refreshedTwoFactor = refreshedActor.Services.GetRequiredService<TwoFactorService>();
        var statusAfterEnable = await refreshedTwoFactor.GetStatusAsync();
        Assert.True(statusAfterEnable.Enabled);
        Assert.Equal(Limits.RecoveryCodesCount, statusAfterEnable.RecoveryCodesLeft);

        // 使用第一組恢復碼停用或兌換後，該組恢復碼不可重複使用。
        var firstCode = recoveryCodes[0];
        var userEntity = await refreshedActor.Users.FindByIdAsync(created.Id);
        Assert.NotNull(userEntity);
        Assert.True(TwoFactorService.TryRedeemRecoveryCode(userEntity, firstCode));
        Assert.False(TwoFactorService.TryRedeemRecoveryCode(userEntity, firstCode));
        await refreshedActor.Users.UpdateAsync(userEntity);

        // 全站管理者可從後台重設一般使用者的 2FA。
        await admin.Sites.ResetUserTwoFactorAsync(created.Id);
        using var resetActor = await App.UserAsync(created.Id);
        var statusAfterReset = await resetActor.Services.GetRequiredService<TwoFactorService>().GetStatusAsync();
        Assert.False(statusAfterReset.Enabled);
        Assert.Equal(0, statusAfterReset.RecoveryCodesLeft);
    }

    private static string Rfc6238Code(byte[] key, long unixSeconds)
    {
        var step = unixSeconds / 30;
        Span<byte> message = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { message[i] = (byte)(step & 0xff); step >>= 8; }
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(message.ToArray());
        var offset = hash[^1] & 0x0f;
        return (((hash[offset] << 24 | hash[offset + 1] << 16 | hash[offset + 2] << 8 | hash[offset + 3]) & 0x7fffffff) % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string value)
    {
        var bits = 0; var buffer = 0; var bytes = new List<byte>();
        foreach (var c in value)
        {
            var index = c switch
            {
                >= 'A' and <= 'Z' => c - 'A',
                >= 'a' and <= 'z' => c - 'a',
                >= '2' and <= '7' => c - '2' + 26,
                _ => -1
            };
            if (index < 0) continue;
            buffer = (buffer << 5) | index; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xff)); bits -= 8; }
        }
        return [.. bytes];
    }
}
