namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class TaxonomyServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Token = "isolated-test-service-token-0123456789abcdef";

    [PostgresTheory]
    [InlineData(TaxonomyKind.Category)]
    [InlineData(TaxonomyKind.Tag)]
    public async Task Merge_moves_relations_removes_source_and_never_duplicates(TaxonomyKind kind)
    {
        using var admin = await App.AdministratorAsync();
        var source = await admin.Taxonomy.SaveAsync(kind, new() { Name = "舊項目" });
        var target = await admin.Taxonomy.SaveAsync(kind, new() { Name = "新項目" });
        var only = NewContent("僅帶來源");
        only.CategoryIds = [source.Id!.Value];
        only.TagIds = [];
        var both = NewContent("兩者皆帶");
        both.CategoryIds = [source.Id!.Value, target.Id!.Value];
        both.TagIds = [];
        if (kind == TaxonomyKind.Tag)
        {
            only.CategoryIds = [];
            only.TagIds = [source.Id!.Value];
            both.CategoryIds = [];
            both.TagIds = [source.Id!.Value, target.Id!.Value];
        }
        await admin.Content.SaveAsync(only);
        await admin.Content.SaveAsync(both);

        var moved = await admin.Taxonomy.MergeAsync(kind, source.Id!.Value, target.Id!.Value, source.Version);

        // 僅「僅帶來源」是新移轉；「兩者皆帶」原本就屬於目標，只移除來源關聯、不計入。
        Assert.Equal(1, moved);
        await using var db = await App.CreateContextAsync();
        Assert.False(await db.Categories.AnyAsync(x => x.Id == source.Id) || await db.Tags.AnyAsync(x => x.Id == source.Id));
        foreach (var title in new[] { "僅帶來源", "兩者皆帶" })
        {
            var content = await db.Contents.AsNoTracking().AsSplitQuery()
                .Include(x => x.Categories).Include(x => x.Tags)
                .SingleAsync(x => x.Title == title);
            if (kind == TaxonomyKind.Category)
            {
                Assert.Equal(target.Id, content.Categories.Single().Id);
                Assert.Empty(content.Tags);
            }
            else
            {
                Assert.Empty(content.Categories);
                Assert.Equal(target.Id, content.Tags.Single().Id);
            }
        }
        // 目標的並發版本在合併後仍可用（合併不覆寫目標內容）。
        var renamed = await admin.Taxonomy.SaveAsync(kind, new TaxonomyEdit { Id = target.Id, Name = "合併後改名", Slug = target.Slug, Version = target.Version });
        Assert.Equal("合併後改名", renamed.Name);
    }

    [PostgresFact]
    public async Task Merge_rejects_self_missing_target_conflict_and_requires_admin()
    {
        using var admin = await App.AdministratorAsync();
        var source = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "來源" });
        var target = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "目標" });
        var sourceId = source.Id!.Value;
        var targetId = target.Id!.Value;

        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Taxonomy.MergeAsync(TaxonomyKind.Category, sourceId, sourceId, source.Version));
        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Taxonomy.MergeAsync(TaxonomyKind.Category, sourceId, Guid.NewGuid(), source.Version));
        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Taxonomy.MergeAsync(TaxonomyKind.Category, sourceId, targetId, Guid.NewGuid()));
        // 不同類不可互相合併：分類來源指向標籤目標視為不存在的目標。
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new() { Name = "標籤" });
        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Taxonomy.MergeAsync(TaxonomyKind.Category, sourceId, tag.Id!.Value, source.Version));

        using var anonymous = App.Anonymous();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            anonymous.Taxonomy.MergeAsync(TaxonomyKind.Category, sourceId, targetId, source.Version));
    }

    [PostgresFact]
    public async Task Merge_endpoint_executes_over_bff()
    {
        using var actor = await App.AdministratorAsync();
        var source = await actor.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "API 來源" });
        var target = await actor.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "API 目標" });
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        // BFF 合約：管理端點要求服務通行證＋有效的管理員工作階段戳記。
        client.DefaultRequestHeaders.Add(AdminSession.StampHeader, await App.AdminSecurityStampAsync());
        client.DefaultRequestHeaders.Add(AccessClaims.UserHeader, await App.AdminUserIdAsync());

        using var self = await client.PostAsJsonAsync($"/api/v1/admin/taxonomies/{source.Id}/merge?kind=Category",
            new { targetId = source.Id, version = source.Version });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);

        using var ok = await client.PostAsJsonAsync($"/api/v1/admin/taxonomies/{source.Id}/merge?kind=Category",
            new { targetId = target.Id, version = source.Version });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = JsonNode.Parse(await ok.Content.ReadAsStringAsync())!;
        Assert.Equal(0, (int)body["moved"]!);

        using var listed = await client.GetAsync("/api/v1/admin/taxonomies?kind=Category");
        var list = JsonNode.Parse(await listed.Content.ReadAsStringAsync())!;
        Assert.Single((JsonArray)list["items"]!);
        Assert.Equal("API 目標", (string)list["items"]![0]!["name"]!);
    }

    [PostgresTheory]
    [InlineData(TaxonomyKind.Category)]
    [InlineData(TaxonomyKind.Tag)]
    public async Task Taxonomy_slugs_are_normalized_unique_per_kind_and_stable_on_rename(TaxonomyKind kind)
    {
        using var admin = await App.AdministratorAsync();
        var item = await admin.Taxonomy.SaveAsync(kind, new() { Name = "  Initial Name  ", Slug = "ＳＡＭＥ__Slug" });
        Assert.Equal("same-slug", item.Slug);
        Assert.Equal("Initial Name", Assert.Single((await admin.Taxonomy.ListAsync(kind)).Items).Name);
        var duplicate = await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.SaveAsync(kind, new() { Name = "Duplicate", Slug = "same / SLUG" }));
        Assert.Contains("代稱已存在", duplicate.Message, StringComparison.Ordinal);
        var opposite = kind == TaxonomyKind.Category ? TaxonomyKind.Tag : TaxonomyKind.Category;
        await admin.Taxonomy.SaveAsync(opposite, new() { Name = "Independent kind", Slug = item.Slug });
        item.Name = "New Name";
        item = await admin.Taxonomy.SaveAsync(kind, item);
        Assert.Equal("same-slug", item.Slug);
        item.Slug = "Explicit New";
        item = await admin.Taxonomy.SaveAsync(kind, item);
        Assert.Equal("explicit-new", item.Slug);
        Assert.Equal(item.Version, Assert.Single((await admin.Taxonomy.ListAsync(kind)).Items).Version);
    }

    [PostgresTheory]
    [InlineData(TaxonomyKind.Category)]
    [InlineData(TaxonomyKind.Tag)]
    public async Task Stale_taxonomy_update_and_delete_do_not_replace_a_newer_version(TaxonomyKind kind)
    {
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync();
        var original = await first.Taxonomy.SaveAsync(kind, new() { Name = "Original" });
        var stale = new TaxonomyEdit { Id = original.Id, Name = "Stale edit", Slug = original.Slug, Version = original.Version };
        original.Name = "Winner";
        var winner = await first.Taxonomy.SaveAsync(kind, original);
        await Assert.ThrowsAsync<CmsException>(() => second.Taxonomy.SaveAsync(kind, stale));
        await Assert.ThrowsAsync<CmsException>(() => second.Taxonomy.DeleteAsync(kind, stale.Id!.Value, stale.Version));
        Assert.Equal("Winner", Assert.Single((await second.Taxonomy.ListAsync(kind)).Items).Name);
        await second.Taxonomy.DeleteAsync(kind, winner.Id!.Value, winner.Version);
        Assert.Empty((await first.Taxonomy.ListAsync(kind)).Items);
        await Assert.ThrowsAsync<CmsException>(() => first.Taxonomy.SaveAsync(kind, winner));
        await Assert.ThrowsAsync<CmsException>(() => first.Taxonomy.DeleteAsync(kind, winner.Id.Value, winner.Version));
    }

    [PostgresTheory]
    [InlineData(TaxonomyKind.Category)]
    [InlineData(TaxonomyKind.Tag)]
    public async Task Deleting_assigned_taxonomy_removes_only_its_joins_and_preserves_all_posts(TaxonomyKind kind)
    {
        using var admin = await App.AdministratorAsync();
        using var visitor = App.Anonymous();
        var removed = await admin.Taxonomy.SaveAsync(kind, new() { Name = "Remove me" });
        var kept = await admin.Taxonomy.SaveAsync(kind, new() { Name = "Keep me" });
        var posts = new List<ContentEdit>();
        for (var index = 0; index < 2; index++)
        {
            var post = NewContent("Post " + index, status: ContentStatus.Published);
            if (kind == TaxonomyKind.Category) post.CategoryIds = [removed.Id!.Value, kept.Id!.Value];
            else post.TagIds = [removed.Id!.Value, kept.Id!.Value];
            posts.Add(await admin.Content.SaveAsync(post));
        }
        await admin.Taxonomy.DeleteAsync(kind, removed.Id!.Value, removed.Version);
        Assert.Equal(kept.Id, Assert.Single((await admin.Taxonomy.ListAsync(kind)).Items).Id);
        foreach (var post in posts)
        {
            var edit = await admin.Content.GetEditAsync(post.Id!.Value);
            Assert.Equal(new[] { kept.Id!.Value }, kind == TaxonomyKind.Category ? edit.CategoryIds : edit.TagIds);
            var published = (await visitor.Content.GetPublishedAsync(ContentKind.Post, post.Slug!))!;
            Assert.Equal([new NavigationItem("Keep me", "keep-me")], kind == TaxonomyKind.Category ? published.Categories : published.Tags);
        }
        await using var db = await App.CreateContextAsync();
        Assert.Equal(2, await db.Contents.CountAsync());
    }

    [PostgresTheory]
    [InlineData(TaxonomyKind.Category)]
    [InlineData(TaxonomyKind.Tag)]
    public async Task Taxonomy_pagination_is_stable_with_duplicate_names_and_checks_boundaries(TaxonomyKind kind)
    {
        using var admin = await App.AdministratorAsync();
        var items = new List<TaxonomyEdit>();
        for (var index = 0; index < 5; index++)
            items.Add(await admin.Taxonomy.SaveAsync(kind, new() { Name = "Tied name", Slug = "item-" + index }));
        var expected = items.Select(x => x.Id!.Value).OrderBy(x => x.ToString("N"), StringComparer.Ordinal).ToArray();
        var actual = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await admin.Taxonomy.ListAsync(kind, page, 2);
            Assert.Equal(5, result.Total);
            Assert.Equal(3, result.TotalPages);
            actual.AddRange(result.Items.Select(x => x.Id));
            Assert.Equal(result.Items.Select(x => x.Id), (await admin.Taxonomy.ListAsync(kind, page, 2)).Items.Select(x => x.Id));
        }
        Assert.Equal(expected, actual);
        Assert.Empty((await admin.Taxonomy.ListAsync(kind, 4, 2)).Items);
        Assert.Equal(Limits.MaxPageSize, (await admin.Taxonomy.ListAsync(kind)).PageSize);
        foreach (var (page, size) in new[] { (0, 10), (1, 0), (1, Limits.MaxPageSize + 1), (int.MaxValue, 2) })
            await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.ListAsync(kind, page, size));
    }

    [PostgresTheory]
    [InlineData(TaxonomyKind.Category)]
    [InlineData(TaxonomyKind.Tag)]
    public async Task Taxonomy_service_validates_lengths_and_accepts_exact_maximums(TaxonomyKind kind)
    {
        using var admin = await App.AdministratorAsync();
        var bad = new[]
        {
            new TaxonomyEdit { Name = " \t" },
            new TaxonomyEdit { Name = new string('n', Limits.TaxonomyName + 1) },
            new TaxonomyEdit { Name = "Valid", Slug = new string('s', Limits.Slug + 1) },
            new TaxonomyEdit { Name = "Valid", Slug = "///" }
        };
        foreach (var input in bad) await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.SaveAsync(kind, input));
        Assert.Empty((await admin.Taxonomy.ListAsync(kind)).Items);
        var valid = await admin.Taxonomy.SaveAsync(kind,
            new() { Name = new string('n', Limits.TaxonomyName), Slug = new string('s', Limits.Slug) });
        var saved = Assert.Single((await admin.Taxonomy.ListAsync(kind)).Items);
        Assert.Equal(valid.Name, saved.Name);
        Assert.Equal(valid.Slug, saved.Slug);
    }

    [PostgresFact]
    public async Task Unknown_taxonomy_kinds_are_rejected_on_every_entry_point()
    {
        using var admin = await App.AdministratorAsync();
        var kind = (TaxonomyKind)99;
        await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.ListAsync(kind));
        await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.SaveAsync(kind, new() { Name = "Invalid kind" }));
        await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.DeleteAsync(kind, Guid.NewGuid(), Guid.NewGuid()));
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.Categories.ToListAsync());
        Assert.Empty(await db.Tags.ToListAsync());
    }

    [PostgresFact]
    public async Task Descriptions_round_trip_and_admin_search_finds_content()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "旅行", Description = "路上的觀察與故事。" });
        var travel = (await admin.Taxonomy.ListAsync(TaxonomyKind.Category)).Items.Single();
        Assert.Equal("路上的觀察與故事。", travel.Description);

        // 分類底下一旦有已發布文章，說明文字就會出現在公開的篩選列表。
        Assert.Null(await admin.Content.GetPublicTaxonomyAsync(TaxonomyKind.Category, "旅行"));
        var post = NewContent("東京散步地圖", status: ContentStatus.Published);
        post.CategoryIds = [travel.Id];
        await admin.Content.SaveAsync(post);
        var publicView = await admin.Content.GetPublicTaxonomyAsync(TaxonomyKind.Category, "旅行");
        Assert.NotNull(publicView);
        Assert.Equal("路上的觀察與故事。", publicView!.Description);

        // 後台搜尋對標題與 slug 逐字比對，包含已逸出的 LIKE 萬用字元。
        await admin.Content.SaveAsync(NewContent("完全不同的主題", status: ContentStatus.Published));
        var byTitle = await admin.Content.ListAdminAsync(ContentKind.Post, search: "東京");
        Assert.Single(byTitle.Items);
        Assert.Equal("東京散步地圖", byTitle.Items.Single().Title);
        var all = await admin.Content.ListAdminAsync(ContentKind.Post, search: "");
        var byWildcard = await admin.Content.ListAdminAsync(ContentKind.Post, search: "%");
        // 萬用字元已逸出，只會逐字比對，絕不會擴大結果集。
        Assert.Equal(0, byWildcard.Total);
        Assert.True(all.Total > 0);
        Assert.Empty((await admin.Content.ListAdminAsync(ContentKind.Post, search: "不存在的主題")).Items);
    }
}
