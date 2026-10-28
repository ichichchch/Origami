namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SiteIsolationTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Scoped_factories_follow_the_actor_site_and_return_independent_live_contexts()
    {
        var site = await CreateSiteAsync();
        using var actor = App.Anonymous();
        actor.Site.SiteId = site.Id; // 必須能在解析服務／factory 前選站。
        var factory = actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>();
        Assert.Same(factory, actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>());
        using var other = App.Anonymous();
        Assert.NotSame(factory, other.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>());
        Assert.Throws<InvalidOperationException>(() => App.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>());
        await using var first = await factory.CreateDbContextAsync();
        await using var second = await App.CreateContextAsync(site.Id);
        await using var defaults = await App.CreateContextAsync();
        Assert.NotSame(first, second);
        Assert.Equal(site.Id, first.CurrentSiteId);
        Assert.Equal(site.Id, (await second.Settings.SingleAsync()).SiteId);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), defaults.CurrentSiteId);
    }

    [PostgresFact]
    public async Task Content_and_taxonomy_slugs_are_unique_per_site_not_per_application()
    {
        var site = await CreateSiteAsync();
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync(site.Id);
        var firstPost = await first.Content.SaveAsync(NewContent("Shared slug", status: ContentStatus.Published));
        var secondPost = await second.Content.SaveAsync(NewContent("Shared slug", status: ContentStatus.Published));
        Assert.Equal(firstPost.Slug, secondPost.Slug);
        Assert.NotEqual(firstPost.Id, secondPost.Id);
        await Assert.ThrowsAsync<CmsException>(() => first.Content.SaveAsync(NewContent("Shared slug", ContentKind.Page)));
        await Assert.ThrowsAsync<CmsException>(() => second.Content.SaveAsync(NewContent("Shared slug")));
        Assert.Equal(firstPost.Id, Assert.Single((await first.Content.ListAdminAsync(ContentKind.Post)).Items).Id);
        Assert.Equal(secondPost.Id, Assert.Single((await second.Content.ListAdminAsync(ContentKind.Post)).Items).Id);
        var firstDenied = await Assert.ThrowsAsync<CmsException>(() => first.Content.GetEditAsync(secondPost.Id!.Value));
        var secondDenied = await Assert.ThrowsAsync<CmsException>(() => second.Content.GetEditAsync(firstPost.Id!.Value));
        Assert.Equal("內容不存在。", firstDenied.Message);
        Assert.Equal("內容不存在。", secondDenied.Message);
        foreach (var kind in new[] { TaxonomyKind.Category, TaxonomyKind.Tag })
        {
            var a = await first.Taxonomy.SaveAsync(kind, new TaxonomyEdit { Name = "Shared term" });
            var b = await second.Taxonomy.SaveAsync(kind, new TaxonomyEdit { Name = "Shared term" });
            Assert.Equal(a.Slug, b.Slug);
            Assert.NotEqual(a.Id, b.Id);
            await Assert.ThrowsAsync<CmsException>(() => first.Taxonomy.SaveAsync(kind, new TaxonomyEdit { Name = "Shared term" }));
            Assert.Equal(a.Id, Assert.Single((await first.Taxonomy.ListAsync(kind)).Items).Id);
            Assert.Equal(b.Id, Assert.Single((await second.Taxonomy.ListAsync(kind)).Items).Id);
        }
    }

    [PostgresFact]
    public async Task Every_tenant_set_and_join_is_filtered_while_users_and_sites_remain_global()
    {
        var site = await CreateSiteAsync();
        var user = await CreateMemberAsync(SiteRole.Reader);
        var adminId = await App.AdminUserIdAsync();
        await SeedGraphAsync(SiteDefaults.DefaultSiteId, adminId);
        await SeedGraphAsync(site.Id, adminId);
        foreach (var id in new[] { SiteDefaults.DefaultSiteId, site.Id })
        {
            await using var db = await App.CreateContextAsync(id);
            await AssertOneSiteAsync<Content>(db, id);
            await AssertOneSiteAsync<Category>(db, id);
            await AssertOneSiteAsync<Tag>(db, id);
            await AssertOneSiteAsync<MediaAsset>(db, id);
            await AssertOneSiteAsync<ContentRevision>(db, id);
            await AssertOneSiteAsync<ContentView>(db, id);
            await AssertOneSiteAsync<ContentSlugRedirect>(db, id);
            await AssertOneSiteAsync<ContentEmbedding>(db, id);
            await AssertOneSiteAsync<EmbeddingJob>(db, id);
            await AssertOneSiteAsync<SiteSettings>(db, id);
            await AssertOneSiteAsync<ApiCredential>(db, id);
            Assert.All(await db.SiteMemberships.ToListAsync(), member => Assert.Equal(id, member.SiteId));
            Assert.NotEmpty(await db.SiteMemberships.ToListAsync());
            foreach (var table in new[] { "ContentCategories", "ContentTags" })
            {
                var rows = await db.Set<Dictionary<string, object>>(table).AsNoTracking().ToListAsync();
                Assert.Equal(id, Assert.Single(rows)["SiteId"]);
                Assert.Equal(2, await db.Set<Dictionary<string, object>>(table).IgnoreQueryFilters().CountAsync());
            }
            Assert.Equal(2, await db.Contents.IgnoreQueryFilters().CountAsync());
            Assert.Equal(2, await db.Sites.CountAsync());
            Assert.Equal(2, await db.Users.CountAsync());
            Assert.True(await db.Users.AnyAsync(row => row.Id == user.Id));
            foreach (var entity in db.Model.GetEntityTypes().Where(entity => entity.FindProperty("SiteId") is not null))
                Assert.True(entity.FindProperty("SiteId")!.IsConcurrencyToken, entity.Name);
        }
    }

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disconnected_attach_cannot_update_or_delete_a_foreign_row_even_with_forged_site(bool delete, bool forgeSite)
    {
        var site = await CreateSiteAsync();
        using var owner = await App.AdministratorAsync(site.Id);
        var saved = await owner.Content.SaveAsync(NewContent("Foreign content"));
        await using (var attacker = await App.CreateContextAsync())
        {
            var detached = new Content
            {
                Id = saved.Id!.Value, SiteId = forgeSite ? SiteDefaults.DefaultSiteId : site.Id,
                Title = saved.Title, Slug = saved.Slug!, Version = saved.Version
            };
            attacker.Attach(detached);
            if (delete) attacker.Remove(detached);
            else
            {
                detached.Title = "Forged overwrite";
                attacker.Entry(detached).Property(row => row.Title).IsModified = true;
            }
            if (forgeSite)
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => attacker.SaveChangesAsync());
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attacker.SaveChangesAsync());
        }
        await using var verify = await App.CreateContextAsync(site.Id);
        var original = await verify.Contents.SingleAsync();
        Assert.Equal(saved.Id, original.Id);
        Assert.Equal(saved.Title, original.Title);
        Assert.Equal(saved.Version, original.Version);
        Assert.Equal(site.Id, original.SiteId);
    }

    [PostgresTheory]
    [InlineData("parent")]
    [InlineData("cover")]
    [InlineData("category")]
    [InlineData("tag")]
    public async Task Cross_site_relationships_fail_at_service_and_database_boundaries(string relation)
    {
        var site = await CreateSiteAsync();
        using var owner = await App.AdministratorAsync(site.Id);
        var parent = await owner.Content.SaveAsync(NewContent("Foreign page", ContentKind.Page));
        var category = await owner.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "Foreign category" });
        var tag = await owner.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "Foreign tag" });
        var mediaId = Guid.NewGuid();
        await using (var seed = await App.CreateContextAsync(site.Id))
        {
            seed.MediaAssets.Add(new MediaAsset { Id = mediaId, FileName = "foreign.png", ContentType = "image/png" });
            await seed.SaveChangesAsync();
        }
        using var local = await App.AdministratorAsync();
        var input = NewContent("Local", relation == "parent" ? ContentKind.Page : ContentKind.Post);
        switch (relation)
        {
            case "parent": input.ParentId = parent.Id; break;
            case "cover": input.CoverMediaId = mediaId; break;
            case "category": input.CategoryIds = [category.Id!.Value]; break;
            case "tag": input.TagIds = [tag.Id!.Value]; break;
        }
        await Assert.ThrowsAsync<CmsException>(() => local.Content.SaveAsync(input));
        await using (var db = await App.CreateContextAsync())
        {
            Assert.Empty(await db.Contents.ToListAsync());
            var row = new Content { Title = "Bypass", Slug = "bypass", Kind = input.Kind };
            if (relation is "parent" or "cover")
            {
                row.ParentId = input.ParentId;
                row.CoverMediaId = input.CoverMediaId;
                db.Contents.Add(row);
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            }
            else
            {
                db.Contents.Add(row);
                await db.SaveChangesAsync();
                // 原生參數化 SQL 明確繞過 guard，驗證複合 FK 本身，而非只測服務查詢。
                var error = relation == "category"
                    ? await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO \"ContentCategories\" (\"SiteId\", \"ContentsId\", \"CategoriesId\") VALUES ({SiteDefaults.DefaultSiteId}, {row.Id}, {category.Id!.Value})"))
                    : await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO \"ContentTags\" (\"SiteId\", \"ContentsId\", \"TagsId\") VALUES ({SiteDefaults.DefaultSiteId}, {row.Id}, {tag.Id!.Value})"));
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
            }
        }
        await using var check = await App.CreateContextAsync();
        Assert.Empty(await check.Set<Dictionary<string, object>>("ContentCategories").ToListAsync());
        Assert.Empty(await check.Set<Dictionary<string, object>>("ContentTags").ToListAsync());
    }

    [PostgresFact]
    public async Task Settings_and_media_storage_are_site_local_and_default_paths_remain_compatible()
    {
        var site = await CreateSiteAsync();
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync(site.Id);
        var defaults = await first.Settings.GetAsync();
        var settings = await second.Settings.GetAsync();
        settings.Name = "Second site only";
        settings.SiteId = SiteDefaults.DefaultSiteId; // 輸入不能改變操作中的站點。
        await second.Settings.SaveAsync(settings);
        Assert.Equal(defaults.Name, (await first.Settings.GetAsync()).Name);
        Assert.Equal("Second site only", (await second.Settings.GetAsync()).Name);
        Assert.Equal(site.Id, (await second.Settings.GetAsync()).SiteId);
        Assert.Equal(Path.Combine(App.Storage.Root, "images"), first.Storage.Images);
        Assert.Equal(Path.Combine(App.Storage.Root, "sites", site.Id.ToString("N"), "images"), second.Storage.Images);
        Assert.NotEqual(first.Storage.Staging, second.Storage.Staging);
        Assert.Equal(first.Storage.Keys, second.Storage.Keys);
        using var bitmap = new SKBitmap(8, 8);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = new MemoryStream(data.ToArray());
        var media = await second.Media.UploadAsync(stream, "same-name.png");
        Assert.True(File.Exists(Path.Combine(second.Storage.Images, media.FileName)));
        Assert.False(File.Exists(Path.Combine(first.Storage.Images, media.FileName)));
        Assert.Empty((await first.Media.ListAsync()).Items);
        Assert.Equal(media.Id, Assert.Single((await second.Media.ListAsync()).Items).Id);
        await Assert.ThrowsAsync<CmsException>(() => first.Media.UpdateMetaAsync(media.Id, "foreign", "foreign"));
        Assert.Equal("", Assert.Single((await second.Media.ListAsync()).Items).AltText);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Embedding_worker_uses_site_scopes_with_both_supported_constructors(bool legacyConstructor)
    {
        var site = await CreateSiteAsync();
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync(site.Id);
        var a = await first.Content.SaveAsync(NewContent("Default work", status: ContentStatus.Published));
        var b = await second.Content.SaveAsync(NewContent("Second work", status: ContentStatus.Published));
        using var worker = legacyConstructor
            ? new EmbeddingJobWorker(first.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(), App.Services,
                TimeProvider.System, NullLogger<EmbeddingJobWorker>.Instance)
            : App.Worker;
        Assert.Equal(2, await worker.ProcessPendingAsync());
        await using var defaults = await App.CreateContextAsync();
        await using var selected = await App.CreateContextAsync(site.Id);
        Assert.Empty(await defaults.EmbeddingJobs.ToListAsync());
        Assert.Empty(await selected.EmbeddingJobs.ToListAsync());
        var defaultVector = Assert.Single(await defaults.ContentEmbeddings.ToListAsync());
        var selectedVector = Assert.Single(await selected.ContentEmbeddings.ToListAsync());
        Assert.Equal(a.Id, defaultVector.ContentId);
        Assert.Equal(SiteDefaults.DefaultSiteId, defaultVector.SiteId);
        Assert.Equal(b.Id, selectedVector.ContentId);
        Assert.Equal(site.Id, selectedVector.SiteId);
    }

    private static async Task AssertOneSiteAsync<T>(CmsDbContext db, Guid siteId) where T : class
    {
        var sites = await db.Set<T>().AsNoTracking().Select(row => EF.Property<Guid>(row, "SiteId")).ToListAsync();
        Assert.Equal(siteId, Assert.Single(sites));
    }

    private async Task SeedGraphAsync(Guid siteId, string userId)
    {
        await using var db = await App.CreateContextAsync(siteId);
        var now = DateTimeOffset.UtcNow;
        var content = new Content
        {
            Title = "Graph", Slug = "graph", Categories = [new Category { Name = "Term", Slug = "term" }],
            Tags = [new Tag { Name = "Term", Slug = "term" }]
        };
        // Added 的 SiteId 預設或偽造值都必須被當前 scope 蓋寫。
        content.SiteId = Guid.NewGuid();
        db.Contents.Add(content);
        db.MediaAssets.Add(new MediaAsset { FileName = "graph.png" });
        db.ContentRevisions.Add(new ContentRevision { Content = content, Title = "Revision", CreatedAt = now });
        db.ContentViews.Add(new ContentView { Content = content, Date = DateOnly.FromDateTime(now.UtcDateTime), Count = 1 });
        db.SlugRedirects.Add(new ContentSlugRedirect { Content = content, OldSlug = "old-graph", CreatedAt = now });
        var vector = new float[Limits.EmbeddingDimensions];
        vector[0] = 1;
        db.ContentEmbeddings.Add(new ContentEmbedding { ContentId = content.Id, ChunkText = "Graph", Embedding = new Vector(vector) });
        db.EmbeddingJobs.Add(new EmbeddingJob { ContentId = content.Id, EnqueuedAt = now, NextAttemptAt = now });
        db.ApiCredentials.Add(new ApiCredential
        {
            UserId = userId, Name = "Fixture", SecretHash = new string('a', 64),
            SecurityStamp = await App.AdminSecurityStampAsync(), CreatedAt = now, ExpiresAt = now.AddDays(1)
        });
        await db.SaveChangesAsync();
        Assert.Equal(siteId, content.SiteId);
    }
}
