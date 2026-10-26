namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PublishingParityTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Sticky_posts_lead_the_public_list_and_pages_cannot_be_sticky()
    {
        using var admin = await App.AdministratorAsync();
        for (var i = 1; i <= 3; i++)
        {
            var post = NewContent("列表文章 " + i, status: ContentStatus.Published);
            post.Slug = "list-post-" + i;
            await admin.Content.SaveAsync(post);
        }
        var edit = await admin.Content.GetEditAsync((await admin.Content.ListPublishedAsync()).Items.Single(x => x.Slug == "list-post-2").Id);
        edit.IsSticky = true;
        await admin.Content.UpdateAsync(edit);

        var listed = await admin.Content.ListPublishedAsync();
        Assert.Equal("list-post-2", listed.Items[0].Slug);
        Assert.True(listed.Items[0].IsSticky);
        Assert.Equal(["list-post-3", "list-post-1"], listed.Items.Skip(1).Select(x => x.Slug));

        // Pages never join the article list, so the flag is refused for them.
        var page = await admin.Content.CreateAsync(ContentKind.Page, NewContent("關於", ContentKind.Page, ContentStatus.Published));
        var pageEdit = await admin.Content.GetEditAsync(page.Id!.Value);
        pageEdit.IsSticky = true;
        var error = await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(pageEdit));
        Assert.Contains("置頂", error.Message, StringComparison.Ordinal);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var home = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("置頂", WebUtility.HtmlDecode(home));
    }

    [PostgresFact]
    public async Task Retired_slugs_redirect_permanently_until_content_leaves_the_public_site()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("搬家的文章", status: ContentStatus.Published);
        post.Slug = "old-address";
        var saved = await admin.Content.SaveAsync(post);
        var rename = await admin.Content.GetEditAsync(saved.Id!.Value);
        rename.Slug = "new-address";
        await admin.Content.UpdateAsync(rename);
        // Renaming a draft-only slug never creates a public URL worth redirecting.
        var draft = NewContent("草稿改名");
        draft.Slug = "draft-before";
        var draftSaved = await admin.Content.SaveAsync(draft);
        var draftRename = await admin.Content.GetEditAsync(draftSaved.Id!.Value);
        draftRename.Slug = "draft-after";
        await admin.Content.UpdateAsync(draftRename);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var moved = await client.GetAsync("/posts/old-address");
        Assert.Equal(HttpStatusCode.MovedPermanently, moved.StatusCode);
        Assert.Equal("/posts/new-address", moved.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/posts/new-address")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/draft-before")).StatusCode);

        // Withdrawal removes the redirect target; the retired slug stops resolving.
        var withdraw = await admin.Content.GetEditAsync(saved.Id!.Value);
        withdraw.Status = ContentStatus.Draft;
        await admin.Content.UpdateAsync(withdraw);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/old-address")).StatusCode);
    }

    [PostgresFact]
    public async Task Renamed_page_slugs_redirect_within_the_page_route()
    {
        using var admin = await App.AdministratorAsync();
        var page = NewContent("關於本站", ContentKind.Page, ContentStatus.Published);
        page.Slug = "about-old";
        var saved = await admin.Content.SaveAsync(page);
        var rename = await admin.Content.GetEditAsync(saved.Id!.Value);
        rename.Slug = "about-new";
        await admin.Content.UpdateAsync(rename);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var moved = await client.GetAsync("/pages/about-old");
        Assert.Equal(HttpStatusCode.MovedPermanently, moved.StatusCode);
        Assert.Equal("/pages/about-new", moved.Headers.Location!.ToString());
    }

    [Fact]
    public void Rendered_content_images_defer_loading()
    {
        var markdown = new MarkdownService();
        var html = markdown.Render("開頭\n\n![封面](/media/cover.png)\n\n![第二張](https://example.org/pic.png)");
        Assert.Equal(2, html.Split(["loading=\"lazy\" decoding=\"async\""], StringSplitOptions.None).Length - 1);
        Assert.Contains("<img loading=\"lazy\" decoding=\"async\" src=\"/media/cover.png\"", html);
    }

    [PostgresFact]
    public async Task Media_alt_text_round_trips_into_lists_and_markdown_snippets()
    {
        using var admin = await App.AdministratorAsync();
        await using var db = await App.CreateContextAsync();
        var asset = new MediaAsset { FileName = "demo.png", OriginalName = "demo.png", ContentType = "image/png", UploadedAt = DateTimeOffset.UtcNow };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();

        using var mediaDirectory = new TemporaryMediaStorage();
        var storage = mediaDirectory.Create();
        MediaService For(TestActor actor) => new(actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            actor.Services.GetRequiredService<AdminAccess>(), storage, TimeProvider.System, NullLogger<MediaService>.Instance);
        var service = For(admin);

        var updated = await service.UpdateAltAsync(asset.Id, "  山林的清晨 [晨霧]  ");
        Assert.Equal("山林的清晨 [晨霧]", updated.AltText);
        Assert.Equal("![山林的清晨 \\[晨霧\\]](/media/demo.png)", updated.Markdown);

        var listed = (await service.ListAsync()).Items.Single();
        Assert.Equal("山林的清晨 [晨霧]", listed.AltText);

        var tooLong = await Assert.ThrowsAsync<CmsException>(() => service.UpdateAltAsync(asset.Id, new string('長', Limits.AltText + 1)));
        Assert.Contains("替代文字", tooLong.Message, StringComparison.Ordinal);

        using (var anonymous = App.Anonymous())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => For(anonymous).UpdateAltAsync(asset.Id, "x"));
        }
    }

    [PostgresFact]
    public async Task Health_endpoint_reports_database_liveness_without_details()
    {
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
        Assert.DoesNotContain("Host=", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", body, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Export_contains_site_data_and_requires_the_administrator()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "導出", Slug = "exported" });
        var post = NewContent("匯出文章 & <特殊> 字元", status: ContentStatus.Published);
        post.Slug = "export-post";
        post.Excerpt = "匯出摘要。";
        post.IsSticky = true;
        post.CategoryIds = [category.Id!.Value];
        await admin.Content.SaveAsync(post);
        await using var db = await App.CreateContextAsync();
        db.MediaAssets.Add(new MediaAsset { FileName = "cover.png", OriginalName = "cover.png", ContentType = "image/png", AltText = "封面圖", UploadedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        using var anonymous = App.Anonymous();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => anonymous.Services.GetRequiredService<ExportService>().WriteAsync(Stream.Null));

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var document = JsonNode.Parse(buffer.ToArray())!;
        Assert.Equal("origami-export", (string?)document["format"]);
        var contents = document["contents"]!.AsArray();
        var exported = contents.Single(x => (string?)x!["slug"] == "export-post")!;
        Assert.Equal("post", (string?)exported["kind"]);
        Assert.Equal("published", (string?)exported["status"]);
        Assert.True((bool?)exported["isSticky"]);
        Assert.Equal("匯出摘要。", (string?)exported["excerpt"]);
        Assert.Equal("Test body", (string?)exported["markdown"]);
        Assert.Equal("exported", exported["categories"]!.AsArray().Single().ToString());
        Assert.Equal("封面圖", (string?)document["media"]!.AsArray().Single()!["altText"]);
        // Account rows never travel with an export.
        Assert.Null(document["users"]);
        Assert.Null(document["admins"]);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var unauthenticated = await client.GetAsync("/admin/export");
        Assert.Equal(HttpStatusCode.Redirect, unauthenticated.StatusCode);
        Assert.Contains("/account/login", unauthenticated.Headers.Location!.ToString());
    }

    [PostgresFact]
    public async Task Import_replaces_text_data_and_reindexes_published_content()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "旅行" });
        var travel = (await admin.Taxonomy.ListAsync(TaxonomyKind.Category)).Items.Single();
        ContentEdit Post(string title, string slug) => new()
        { Title = title, Slug = slug, Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = "內文。", CategoryIds = [travel.Id] };
        var keep = await admin.Content.SaveAsync(Post("保留的文章", "keep-post"));
        var secret = Post("私密文章", "secret-post");
        secret.IsPrivate = true;
        await admin.Content.SaveAsync(secret);
        var parent = await admin.Content.CreateAsync(ContentKind.Page, new() { Title = "上層頁面", Slug = "parent-page", Kind = ContentKind.Page, Status = ContentStatus.Published, Markdown = "頁面。" });
        await admin.Content.CreateAsync(ContentKind.Page, new() { Title = "下層頁面", Slug = "child-page", Kind = ContentKind.Page, Status = ContentStatus.Published, Markdown = "頁面。", ParentId = parent.Id });
        var settings = await admin.Settings.GetAsync();
        settings.PostsPerPage = 3;
        await admin.Settings.SaveAsync(settings);
        settings = await admin.Settings.GetAsync();
        settings.HomePageContentId = parent.Id;
        await admin.Settings.SaveAsync(settings);

        // A stray post created after the export must disappear with the restore-style import.
        var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        await admin.Content.SaveAsync(Post("不該存在的文章", "stray-post"));

        var importer = admin.Services.GetRequiredService<ImportService>();
        using (var input = new MemoryStream(buffer.ToArray()))
        {
            var preview = await importer.AnalyzeAsync(input);
            Assert.Equal("Origami", preview.SiteName);
            Assert.Equal(2, preview.Posts);
            Assert.Equal(2, preview.Pages);
        }
        await Assert.ThrowsAsync<CmsException>(() =>
        {
            using var input = new MemoryStream(buffer.ToArray());
            return importer.ApplyAsync(input, confirmed: false);
        });
        using (var input = new MemoryStream("not json"u8.ToArray()))
        {
            await Assert.ThrowsAsync<CmsException>(() => importer.AnalyzeAsync(input));
        }

        using (var input = new MemoryStream(buffer.ToArray()))
        {
            await importer.ApplyAsync(input, confirmed: true);
        }

        await using (var db = await App.CreateContextAsync())
        {
            Assert.True(await db.Contents.SingleOrDefaultAsync(x => x.Slug == "stray-post") is null, "import replaces, it never merges");
            var importedKeep = await db.Contents.AsNoTracking().Include(x => x.Categories).SingleAsync(x => x.Slug == "keep-post");
            Assert.Equal(ContentStatus.Published, importedKeep.Status);
            Assert.Equal("旅行", importedKeep.Categories.Single().Name);
            Assert.True(await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == "secret-post") is { IsPrivate: true });
            var child = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == "child-page");
            var importedParent = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == "parent-page");
            Assert.Equal(importedParent.Id, child.ParentId);
            var restored = await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1);
            Assert.Equal(3, restored.PostsPerPage);
            Assert.Equal(importedParent.Id, restored.HomePageContentId);
            // Only published public content re-enters the embedding pipeline (post + both public pages).
            var queued = await db.EmbeddingJobs.CountAsync();
            Assert.Equal(3, queued);
            var queuedIds = await db.EmbeddingJobs.Select(x => x.ContentId).ToListAsync();
            Assert.Contains(queuedIds, id => id == importedKeep.Id);
        }
    }
}
