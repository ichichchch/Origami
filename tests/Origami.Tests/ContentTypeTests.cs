namespace Origami.Tests;

/// <summary>
/// 自訂內容類型與自訂分類法：站點隔離、公開專區與文章列表分離、詞彙驗證、
/// 類型旗標限制分類標籤、封存與刪除規則。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentTypeTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static ContentTypeService TypeService(TestActor actor) =>
        actor.Services.GetRequiredService<ContentTypeService>();

    [PostgresFact]
    public async Task Types_are_isolated_per_site_and_reject_duplicate_or_reserved_slugs()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var created = await service.SaveAsync(new ContentTypeService.TypeEdit
        {
            Name = "書評", Slug = "reviews", SupportsCategories = true, SupportsTags = true
        });
        Assert.Equal("reviews", created.Slug);
        Assert.Equal(0, created.ContentCount);

        // 同一站點不可重複代稱；系統保留前綴也拒絕。
        await Assert.ThrowsAsync<CmsException>(() => service.SaveAsync(new ContentTypeService.TypeEdit
        { Name = "另一個書評", Slug = "reviews" }));
        await Assert.ThrowsAsync<CmsException>(() => service.SaveAsync(new ContentTypeService.TypeEdit
        { Name = "保留代稱", Slug = ContentTypeService.PublicPrefix }));

        // 另一站點看不到本站的類型，也可以在自己的站點使用相同代稱。
        var site = await CreateSiteAsync();
        using var siteAdmin = await App.AdministratorAsync(site.Id);
        var siteService = TypeService(siteAdmin);
        Assert.Empty(await siteService.ListAsync());
        var siteType = await siteService.SaveAsync(new ContentTypeService.TypeEdit { Name = "本站書評", Slug = "reviews" });
        Assert.Equal("reviews", siteType.Slug);
        Assert.Single(await service.ListAsync());
        Assert.Single(await siteService.ListAsync());
    }

    [PostgresFact]
    public async Task Custom_content_publishes_to_its_own_section_and_stays_out_of_the_post_list()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var type = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var content = await admin.Content.SaveAsync(new ContentEdit
        {
            Title = "一本好書", Kind = ContentKind.Post, Status = ContentStatus.Published,
            Markdown = "正文", ContentTypeId = type.Id
        });

        // 自訂類型內容不進入文章列表，也不會出現在 /posts/{slug}。
        var posts = await admin.Content.ListPublishedAsync();
        Assert.DoesNotContain(posts.Items, x => x.Id == content.Id);
        Assert.Null(await App.Anonymous().Content.GetPublishedAsync(ContentKind.Post, content.Slug!));

        // 只在自己的公開專區出現。
        var section = await admin.Content.ListByTypeAsync("reviews");
        Assert.NotNull(section);
        Assert.Equal(1, section.Total);
        Assert.Equal("一本好書", Assert.Single(section.Items).Title);
        var detail = await App.Anonymous().Content.GetPublishedByTypeAsync("reviews", content.Slug!);
        Assert.NotNull(detail);
        Assert.Equal("一本好書", detail!.Title);
        Assert.Null(await admin.Content.ListByTypeAsync("missing-type"));
    }

    [PostgresFact]
    public async Task Type_flags_restrict_categories_and_tags()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var plain = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "純內容", Slug = "plain" });
        var rich = await service.SaveAsync(new ContentTypeService.TypeEdit
        {
            Name = "有分類", Slug = "rich", SupportsCategories = true, SupportsTags = true
        });
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "分類" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "標籤" });

        var refused = NewContent("不該有分類", status: ContentStatus.Draft);
        refused.ContentTypeId = plain.Id;
        refused.CategoryIds = [category.Id!.Value];
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(refused));

        var allowed = NewContent("可以有分類", status: ContentStatus.Draft);
        allowed.ContentTypeId = rich.Id;
        allowed.CategoryIds = [category.Id!.Value];
        allowed.TagIds = [tag.Id!.Value];
        var saved = await admin.Content.SaveAsync(allowed);
        Assert.Equal(rich.Id, saved.ContentTypeId);
        Assert.Equal([category.Id!.Value], saved.CategoryIds);
    }

    [PostgresFact]
    public async Task Terms_must_belong_to_the_content_type_and_custom_types_cannot_be_changed()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var reviews = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var essays = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "隨筆", Slug = "essays" });
        var genre = await service.SaveTaxonomyAsync(new ContentTypeService.TaxonomyEdit
        { ContentTypeId = reviews.Id, Name = "體裁", Slug = "genre" });
        var essayTag = await service.SaveTaxonomyAsync(new ContentTypeService.TaxonomyEdit
        { ContentTypeId = essays.Id, Name = "主題", Slug = "topic" });
        var term = await service.SaveTermAsync(new ContentTypeService.TermEdit { TaxonomyId = genre.Id, Name = "小說", Slug = "novel" });
        var foreign = await service.SaveTermAsync(new ContentTypeService.TermEdit { TaxonomyId = essayTag.Id, Name = "隨筆", Slug = "misc" });

        var content = NewContent("小說推薦", status: ContentStatus.Draft);
        content.ContentTypeId = reviews.Id;
        content.CustomTermIds = [term.Id];
        var saved = await admin.Content.SaveAsync(content);
        Assert.Equal([term.Id], saved.CustomTermIds);

        // 別的類型的詞彙不可指派；內建文章也不可指派自訂詞彙。
        var wrong = await admin.Content.GetEditAsync(saved.Id!.Value);
        wrong.CustomTermIds = [foreign.Id];
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(wrong));

        var plain = NewContent("一般文章", status: ContentStatus.Draft);
        plain.CustomTermIds = [term.Id];
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(plain));

        // 既有內容的類型不可變更。
        var change = await admin.Content.GetEditAsync(saved.Id.Value);
        change.ContentTypeId = essays.Id;
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(change));
        Assert.Equal(reviews.Id, (await admin.Content.GetEditAsync(saved.Id.Value)).ContentTypeId);
    }

    [PostgresFact]
    public async Task Archived_types_stop_accepting_content_and_types_with_content_cannot_be_deleted()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var type = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var content = NewContent("一本好書", status: ContentStatus.Draft);
        content.ContentTypeId = type.Id;
        var saved = await admin.Content.SaveAsync(content);

        // 仍有內容時不得刪除，只能封存。
        await Assert.ThrowsAsync<CmsException>(() => service.DeleteAsync(type.Id, type.Version));
        var archived = await service.SaveAsync(new ContentTypeService.TypeEdit
        {
            Id = type.Id, Name = type.Name, Slug = type.Slug, IsArchived = true, Version = type.Version
        });
        Assert.True(archived.IsArchived);

        // 封存後不再接受新內容，公開專區也一併關閉。
        var refused = NewContent("新的書評", status: ContentStatus.Draft);
        refused.ContentTypeId = type.Id;
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(refused));
        Assert.Null(await admin.Content.ListByTypeAsync("reviews"));

        // 清空內容後即可刪除類型。
        await admin.Content.TrashAsync(saved.Id!.Value, saved.Version, confirmed: true);
        var trashed = await admin.Content.GetEditAsync(saved.Id.Value);
        await admin.Content.DeleteAsync(saved.Id.Value, trashed.Version, confirmed: true);
        var reloaded = await service.GetAsync(type.Id);
        Assert.NotNull(reloaded);
        await service.DeleteAsync(type.Id, reloaded!.Version);
        Assert.Empty(await service.ListAsync());
    }

    [PostgresFact]
    public async Task Sitemap_lists_custom_type_content_under_its_own_prefix()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var type = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var content = NewContent("一本好書", status: ContentStatus.Published);
        content.ContentTypeId = type.Id;
        var saved = await admin.Content.SaveAsync(content);

        var sitemap = await admin.Content.SitemapAsync();
        Assert.Contains(sitemap, x => x.Path == "/t/reviews/" + saved.Slug);
        Assert.DoesNotContain(sitemap, x => x.Path == "/posts/" + saved.Slug);
    }

    [PostgresFact]
    public async Task Content_types_survive_the_export_and_import_round_trip()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var type = await service.SaveAsync(new ContentTypeService.TypeEdit
        {
            Name = "書評", Slug = "reviews", SupportsCategories = true, SupportsTags = true
        });
        var taxonomy = await service.SaveTaxonomyAsync(new ContentTypeService.TaxonomyEdit
        { ContentTypeId = type.Id, Name = "體裁", Slug = "genre" });
        var term = await service.SaveTermAsync(new ContentTypeService.TermEdit
        { TaxonomyId = taxonomy.Id, Name = "小說", Slug = "novel" });
        var content = NewContent("小說推薦", status: ContentStatus.Published);
        content.ContentTypeId = type.Id;
        content.CustomTermIds = [term.Id];
        await admin.Content.SaveAsync(content);

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        using var input = new MemoryStream(buffer.ToArray());
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        await using var db = await App.CreateContextAsync();
        var restoredType = await db.ContentTypes.AsNoTracking().SingleAsync(x => x.Slug == "reviews");
        Assert.Equal("書評", restoredType.Name);
        Assert.True(restoredType.SupportsCategories);
        var restoredContent = await db.Contents.AsNoTracking().SingleAsync(x => x.Title == "小說推薦");
        Assert.Equal(restoredType.Id, restoredContent.ContentTypeId);
        var restoredTerm = await db.CustomTaxonomyTerms.AsNoTracking().SingleAsync(x => x.Slug == "novel");
        Assert.True(await db.ContentCustomTerms.AsNoTracking().AnyAsync(x =>
            x.ContentId == restoredContent.Id && x.TermId == restoredTerm.Id));
    }

    [PostgresFact]
    public async Task Public_api_exposes_types_and_their_published_contents()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var type = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var content = NewContent("一本好書", status: ContentStatus.Published);
        content.ContentTypeId = type.Id;
        var saved = await admin.Content.SaveAsync(content);

        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();

        using var types = await client.GetAsync("/api/v1/types");
        Assert.Equal(HttpStatusCode.OK, types.StatusCode);
        var typeList = JsonNode.Parse(await types.Content.ReadAsStringAsync())!.AsArray();
        Assert.Contains(typeList, node => (string?)node!["slug"] == "reviews");

        using var list = await client.GetAsync("/api/v1/types/reviews/contents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("一本好書", await list.Content.ReadAsStringAsync());

        using var detail = await client.GetAsync($"/api/v1/types/reviews/contents/{saved.Slug}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        using var missing = await client.GetAsync("/api/v1/types/unknown/contents");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
