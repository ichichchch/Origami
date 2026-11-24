namespace Origami.Tests;

/// <summary>
/// 資料匯入、網站圖示與媒體引用保護的回歸測試。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportExportRegressionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Import_refuses_media_names_that_would_leave_the_image_directory()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("匯入前文章", status: ContentStatus.Published));
        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var document = JsonNode.Parse(buffer.ToArray())!;
        document["media"] = new JsonArray(new JsonObject
        {
            ["fileName"] = "../escape.png",
            ["originalName"] = "escape.png",
            ["contentType"] = "image/png",
            ["altText"] = "",
            ["size"] = 10,
            ["width"] = 10,
            ["height"] = 10,
            ["uploadedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        });

        var importer = admin.Services.GetRequiredService<ImportService>();
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
        var refused = await Assert.ThrowsAsync<CmsException>(() => importer.ApplyAsync(input, confirmed: true));
        Assert.Contains("媒體檔名", refused.Message, StringComparison.Ordinal);

        // 被拒絕的匯入不得改動任何資料。
        await using var db = await App.CreateContextAsync();
        Assert.True(await db.Contents.AnyAsync(x => x.Slug == "匯入前文章" || x.Title == "匯入前文章"));
    }

    [PostgresFact]
    public async Task Import_refuses_unsafe_comment_navigation_and_social_urls_without_replacing_data()
    {
        using var admin = await App.AdministratorAsync();
        var existing = await admin.Content.SaveAsync(NewContent("匯入前文章", status: ContentStatus.Published));
        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var exported = JsonNode.Parse(buffer.ToArray())!;
        var importer = admin.Services.GetRequiredService<ImportService>();

        foreach (var target in new[] { "social", "comment", "navigation" })
        {
            var document = exported.DeepClone();
            if (target == "social")
                document["site"]!["socialGithubUrl"] = "javascript:alert(1)";
            else if (target == "comment")
            {
                document["comments"] = new JsonArray(new JsonObject
                {
                    ["contentSlug"] = existing.Slug,
                    ["authorName"] = "訪客",
                    ["url"] = "javascript:alert(1)",
                    ["markdown"] = "留言"
                });
            }
            else
            {
                document["navigation"] = new JsonArray(new JsonObject
                {
                    ["title"] = "危險導覽",
                    ["url"] = "javascript:alert(1)"
                });
            }

            using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
            var refused = await Assert.ThrowsAsync<CmsException>(() => importer.ApplyAsync(input, confirmed: true));
            Assert.Contains("http 或 https", refused.Message, StringComparison.Ordinal);
        }

        await using var db = await App.CreateContextAsync();
        Assert.True(await db.Contents.AnyAsync(x => x.Title == "匯入前文章"));
        Assert.Empty(await db.SiteNavItems.ToListAsync());
        Assert.Empty(await db.Comments.ToListAsync());
        Assert.Equal("", (await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1)).SocialGithubUrl);
    }

    [PostgresFact]
    public async Task Export_import_round_trips_navigation_relationships_locations_and_targets()
    {
        using var admin = await App.AdministratorAsync();
        var author = await CreateMemberAsync(SiteRole.Author, name: "roundtrip-author");
        var membership = Assert.Single(await admin.Sites.ListMembersAsync(), x => x.UserId == author.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = membership.UserName, Role = membership.Role, DisplayName = "往返作者", Version = membership.Version
        });
        var page = NewContent("關於我們", kind: ContentKind.Page, status: ContentStatus.Published);
        page.Slug = "about";
        var savedPage = await admin.Content.SaveAsync(page);
        var post = await admin.Content.SaveAsync(NewContent("作者文章", status: ContentStatus.Published));
        var postEdit = await admin.Content.GetEditAsync(post.Id!.Value);
        postEdit.AuthorId = author.Id;
        await admin.Content.SaveAsync(postEdit);
        string authorSlug;
        await using (var db = await App.CreateContextAsync())
            authorSlug = (await db.Contents.AsNoTracking().SingleAsync(x => x.Id == post.Id)).AuthorSlug;
        Assert.Equal("往返作者", authorSlug);

        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "生活", Slug = "life" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "週記", Slug = "weekly" });
        var navigation = admin.Services.GetRequiredService<NavigationService>();
        var parent = await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "關於", ContentId = savedPage.Id, Location = 1
        });
        var child = await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "生活隨筆", CategoryId = category.Id, ParentId = parent.Id, Location = 1
        });
        await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "每週", TagId = tag.Id, Location = 1 });
        await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "歸檔", ToArchives = true, Location = 1 });
        await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "作者", AuthorSlug = authorSlug, Location = 1 });

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var exportedBytes = buffer.ToArray();
        var document = JsonNode.Parse(exportedBytes)!;
        var nodes = document["navigation"]!.AsArray();
        Assert.Equal(5, nodes.Count);
        Assert.Equal(savedPage.Slug, (string?)nodes[0]!["pageSlug"]);
        Assert.Equal(1, nodes[0]!["location"]!.GetValue<int>());
        Assert.Null(nodes[0]!["parentIndex"]);
        Assert.Equal(0, nodes[1]!["parentIndex"]!.GetValue<int>());
        Assert.Equal(category.Slug, (string?)nodes[1]!["categorySlug"]);
        Assert.Equal(tag.Slug, (string?)nodes[2]!["tagSlug"]);
        Assert.True(nodes[3]!["toArchives"]!.GetValue<bool>());
        Assert.Equal(authorSlug, (string?)nodes[4]!["authorSlug"]);
        Assert.All(nodes, x => Assert.Equal(1, x!["location"]!.GetValue<int>()));
        Assert.DoesNotContain(parent.Id.ToString(), System.Text.Encoding.UTF8.GetString(exportedBytes), StringComparison.OrdinalIgnoreCase);

        using var input = new MemoryStream(exportedBytes);
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        await using var restoredDb = await App.CreateContextAsync();
        var restoredItems = await restoredDb.SiteNavItems.AsNoTracking().OrderBy(x => x.Position).ToListAsync();
        var restoredPage = await restoredDb.Contents.AsNoTracking().SingleAsync(x => x.Slug == savedPage.Slug && x.Kind == ContentKind.Page);
        var restoredCategory = await restoredDb.Categories.AsNoTracking().SingleAsync(x => x.Slug == category.Slug);
        var restoredTag = await restoredDb.Tags.AsNoTracking().SingleAsync(x => x.Slug == tag.Slug);
        Assert.Equal(5, restoredItems.Count);
        Assert.NotEqual(parent.Id, restoredItems[0].Id);
        Assert.Equal(restoredPage.Id, restoredItems[0].ContentId);
        Assert.Equal(restoredItems[0].Id, restoredItems[1].ParentId);
        Assert.Equal(restoredCategory.Id, restoredItems[1].CategoryId);
        Assert.Equal(restoredTag.Id, restoredItems[2].TagId);
        Assert.True(restoredItems[3].ToArchives);
        Assert.Equal(authorSlug, restoredItems[4].AuthorSlug);
        Assert.All(restoredItems, x => Assert.Equal(1, x.Location));
        Assert.All(restoredItems.Skip(1), x => Assert.Equal("", x.Url));
    }

    [PostgresFact]
    public async Task Import_accepts_legacy_v3_navigation_without_expanded_fields()
    {
        using var admin = await App.AdministratorAsync();
        var page = NewContent("舊版頁面", kind: ContentKind.Page, status: ContentStatus.Published);
        page.Slug = "legacy-page";
        var savedPage = await admin.Content.SaveAsync(page);
        var navigation = admin.Services.GetRequiredService<NavigationService>();
        await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "頁面", ContentId = savedPage.Id, Location = 1 });
        await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "外連", Url = "https://example.com/legacy", Location = 1
        });

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var document = JsonNode.Parse(buffer.ToArray())!;
        foreach (var node in document["navigation"]!.AsArray())
        {
            var item = Assert.IsType<JsonObject>(node);
            item.Remove("location");
            item.Remove("toArchives");
        }

        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        await using var db = await App.CreateContextAsync();
        var restored = await db.SiteNavItems.AsNoTracking().OrderBy(x => x.Position).ToListAsync();
        Assert.Equal(2, restored.Count);
        Assert.All(restored, x =>
        {
            Assert.Equal(0, x.Location);
            Assert.Null(x.ParentId);
            Assert.Null(x.CategoryId);
            Assert.Null(x.TagId);
            Assert.False(x.ToArchives);
            Assert.Equal("", x.AuthorSlug);
        });
        Assert.NotNull(restored[0].ContentId);
        Assert.Equal("https://example.com/legacy", restored[1].Url);
    }

    [PostgresFact]
    public async Task Import_restores_the_site_icon_to_the_imported_media()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var media = CreateMediaService(admin, directory);
        MediaItem icon;
        using (var upload = new MemoryStream(Png(64, 64)))
        {
            icon = await media.UploadAsync(upload, "icon.png");
        }
        var settings = await admin.Settings.GetAsync();
        settings.SiteIconMediaId = icon.Id;
        await admin.Settings.SaveAsync(settings);

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var importer = admin.Services.GetRequiredService<ImportService>();
        using (var input = new MemoryStream(buffer.ToArray()))
        {
            await importer.ApplyAsync(input, confirmed: true);
        }

        var restored = await admin.Settings.GetAsync();
        Assert.NotNull(restored.SiteIconMediaId);
        var restoredIcon = (await media.ListAsync()).Items.Single(x => x.Id == restored.SiteIconMediaId);
        Assert.Equal(icon.FileName, restoredIcon.FileName);
        var file = await media.GetSiteIconAsync();
        Assert.NotNull(file);
    }

    [PostgresFact]
    public async Task A_referenced_variant_protects_the_original_image()
    {
        using var directory = new TemporaryMediaStorage();
        using var admin = await App.AdministratorAsync();
        var media = CreateMediaService(admin, directory);
        MediaItem uploaded;
        using (var upload = new MemoryStream(Png(1200, 400)))
        {
            uploaded = await media.UploadAsync(upload, "variant.png");
        }
        var variant = MediaService.VariantName(uploaded.FileName, 480);
        var post = NewContent("引用縮圖的文章", status: ContentStatus.Published);
        post.Markdown = "![縮圖](/media/" + variant + ")";
        await admin.Content.SaveAsync(post);

        Assert.Equal(1, await media.CountReferencesAsync(uploaded.Id));
        var refused = await Assert.ThrowsAsync<CmsException>(() => media.DeleteAsync(uploaded.Id, confirmed: true));
        Assert.Contains("引用縮圖的文章", refused.Message, StringComparison.Ordinal);
    }

    private static MediaService CreateMediaService(TestActor actor, TemporaryMediaStorage directory) => new(
        actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
        actor.Services.GetRequiredService<AdminAccess>(), directory.Create(), TimeProvider.System, NullLogger<MediaService>.Instance,
        actor.Services.GetRequiredService<IHttpClientFactory>(),
        actor.Services.GetRequiredService<StorageMutationCoordinator>());

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        bitmap.Erase(SKColors.SeaGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90)!;
        return encoded.ToArray();
    }
}
