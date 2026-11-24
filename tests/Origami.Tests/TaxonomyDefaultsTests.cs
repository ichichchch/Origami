namespace Origami.Tests;

/// <summary>站點預設分類／標籤的套用時機，以及標籤轉分類的關聯移轉。</summary>
[Collection(PostgresCollection.Name)]
public sealed class TaxonomyDefaultsTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task New_posts_receive_defaults_only_for_unassigned_kinds()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "預設分類" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "預設標籤" });
        var other = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "其他分類" });
        var settings = await admin.Settings.GetAsync();
        settings.DefaultCategoryId = category.Id;
        settings.DefaultTagId = tag.Id;
        await admin.Settings.SaveAsync(settings);

        var unassigned = await admin.Content.SaveAsync(NewContent("未指派文章"));
        Assert.Equal([category.Id!.Value], unassigned.CategoryIds);
        Assert.Equal([tag.Id!.Value], unassigned.TagIds);

        // 已指派的種類維持原選擇，未指派的種類才套用預設。
        var assigned = NewContent("已指派文章");
        assigned.CategoryIds = [other.Id!.Value];
        var saved = await admin.Content.SaveAsync(assigned);
        Assert.Equal([other.Id!.Value], saved.CategoryIds);
        Assert.Equal([tag.Id!.Value], saved.TagIds);

        // 獨立頁面不可指派分類標籤，因此不套用預設。
        var page = await admin.Content.SaveAsync(NewContent("獨立頁面", kind: ContentKind.Page));
        Assert.Empty(page.CategoryIds);
        Assert.Empty(page.TagIds);

        // 既有內容更新時不會被補上預設。
        assigned.Id = saved.Id;
        assigned.Version = saved.Version;
        assigned.CategoryIds = [];
        assigned.TagIds = [];
        var cleared = await admin.Content.SaveAsync(assigned);
        Assert.Empty(cleared.CategoryIds);
        Assert.Empty(cleared.TagIds);

        // 刪除預設分類後設定自動失效，新文章不再套用。
        await admin.Taxonomy.DeleteAsync(TaxonomyKind.Category, category.Id.Value, category.Version);
        var reloaded = await admin.Settings.GetAsync();
        Assert.Null(reloaded.DefaultCategoryId);
        Assert.Equal(tag.Id, reloaded.DefaultTagId);
        var afterDelete = await admin.Content.SaveAsync(NewContent("刪除後文章"));
        Assert.Empty(afterDelete.CategoryIds);
        Assert.Equal([tag.Id!.Value], afterDelete.TagIds);
    }

    [PostgresFact]
    public async Task Settings_reject_defaults_that_do_not_exist()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.DefaultCategoryId = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(settings));
        Assert.Contains("預設分類不存在", error.Message, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Tag_converts_to_category_and_moves_associations()
    {
        using var admin = await App.AdministratorAsync();
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "旅行", Description = "旅行相關" });
        var post = NewContent("旅行文章", status: ContentStatus.Published);
        post.TagIds = [tag.Id!.Value];
        var saved = await admin.Content.SaveAsync(post);
        var settings = await admin.Settings.GetAsync();
        settings.DefaultTagId = tag.Id;
        settings.CustomNavigationEnabled = true;
        await admin.Settings.SaveAsync(settings);
        var navigation = admin.Services.GetRequiredService<NavigationService>();
        var nav = await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "旅行", TagId = tag.Id });

        var category = await admin.Taxonomy.ConvertTagToCategoryAsync(tag.Id.Value, tag.Version);
        Assert.Equal("旅行", category.Name);
        Assert.Equal(tag.Slug, category.Slug);
        Assert.Equal("旅行相關", category.Description);
        // 標籤已不存在；文章關聯改指到分類，標籤關聯清空。
        await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.DeleteAsync(TaxonomyKind.Tag, tag.Id.Value, tag.Version));
        var detail = await admin.Content.GetEditAsync(saved.Id!.Value);
        Assert.Equal([category.Id], detail.CategoryIds);
        Assert.Empty(detail.TagIds);
        Assert.Null((await admin.Settings.GetAsync()).DefaultTagId);
        var remapped = Assert.Single(await navigation.ListAsync());
        Assert.Equal(nav.Id, remapped.Id);
        Assert.Equal(category.Id, remapped.CategoryId);
        Assert.Null(remapped.TagId);
        Assert.Equal("/category/" + Uri.EscapeDataString(category.Slug), Assert.Single(await navigation.PublicAsync()).Url);

        // 名稱或代稱與既有分類相同時沿用該分類，不建立第二個。
        var duplicate = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "旅行", Slug = "travel-2" });
        var again = await admin.Taxonomy.ConvertTagToCategoryAsync(duplicate.Id!.Value, duplicate.Version);
        Assert.Equal(category.Id, again.Id);

        // 過期的並發版本被拒絕。
        var stale = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "舊版本" });
        await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Id = stale.Id, Version = stale.Version, Slug = stale.Slug, Name = "新名稱" });
        await Assert.ThrowsAnyAsync<Exception>(() => admin.Taxonomy.ConvertTagToCategoryAsync(stale.Id!.Value, stale.Version));
    }

    [PostgresFact]
    public async Task Merging_the_default_term_moves_the_setting_to_the_target()
    {
        using var admin = await App.AdministratorAsync();
        var source = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "來源" });
        var target = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "目標" });
        var settings = await admin.Settings.GetAsync();
        settings.DefaultCategoryId = source.Id;
        settings.CustomNavigationEnabled = true;
        await admin.Settings.SaveAsync(settings);
        var navigation = admin.Services.GetRequiredService<NavigationService>();
        var nav = await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "來源", CategoryId = source.Id });

        await admin.Taxonomy.MergeAsync(TaxonomyKind.Category, source.Id!.Value, target.Id!.Value, source.Version);
        Assert.Equal(target.Id, (await admin.Settings.GetAsync()).DefaultCategoryId);
        var remapped = Assert.Single(await navigation.ListAsync());
        Assert.Equal(nav.Id, remapped.Id);
        Assert.Equal(target.Id, remapped.CategoryId);
        Assert.Null(remapped.TagId);
        Assert.Equal("/category/" + Uri.EscapeDataString(target.Slug!), Assert.Single(await navigation.PublicAsync()).Url);
    }

    [PostgresTheory]
    [InlineData(TaxonomyKind.Category)]
    [InlineData(TaxonomyKind.Tag)]
    public async Task Deleting_a_term_removes_its_navigation_target_and_promotes_children(TaxonomyKind kind)
    {
        using var admin = await App.AdministratorAsync();
        var term = await admin.Taxonomy.SaveAsync(kind, new TaxonomyEdit { Name = "被刪除詞彙" });
        var settings = await admin.Settings.GetAsync();
        settings.CustomNavigationEnabled = true;
        await admin.Settings.SaveAsync(settings);
        var navigation = admin.Services.GetRequiredService<NavigationService>();
        var parent = await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "詞彙導覽",
            CategoryId = kind == TaxonomyKind.Category ? term.Id : null,
            TagId = kind == TaxonomyKind.Tag ? term.Id : null
        });
        var child = await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "子導覽",
            ParentId = parent.Id,
            Url = "https://example.com/child"
        });

        await admin.Taxonomy.DeleteAsync(kind, term.Id!.Value, term.Version);

        var remaining = Assert.Single(await navigation.ListAsync());
        Assert.Equal(child.Id, remaining.Id);
        Assert.Null(remaining.ParentId);
        Assert.Equal("https://example.com/child", remaining.Url);
        var publicItem = Assert.Single(await navigation.PublicAsync());
        Assert.Equal(child.Id, publicItem.Id);
        Assert.Null(publicItem.ParentId);
    }

    [PostgresFact]
    public async Task Merge_rolls_back_bulk_updates_when_version_check_fails()
    {
        using var admin = await App.AdministratorAsync();
        var source = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "來源" });
        var child = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "子分類", ParentId = source.Id });
        var target = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "目標" });
        var settings = await admin.Settings.GetAsync();
        settings.DefaultCategoryId = source.Id;
        await admin.Settings.SaveAsync(settings);
        var navigation = admin.Services.GetRequiredService<NavigationService>();
        var nav = await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "來源", CategoryId = source.Id });
        var staleVersion = source.Version;
        source.Name = "來源已更新";
        var current = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, source);

        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Taxonomy.MergeAsync(TaxonomyKind.Category, source.Id!.Value, target.Id!.Value, staleVersion));

        await using var db = await App.CreateContextAsync();
        var persistedSource = await db.Categories.SingleAsync(x => x.Id == source.Id);
        Assert.Equal(current.Version, persistedSource.Version);
        Assert.Equal("來源已更新", persistedSource.Name);
        Assert.Equal(source.Id, (await db.Categories.SingleAsync(x => x.Id == child.Id)).ParentId);
        Assert.Equal(source.Id, (await db.Settings.SingleAsync(x => x.Id == 1)).DefaultCategoryId);
        var persistedNav = await db.SiteNavItems.SingleAsync(x => x.Id == nav.Id);
        Assert.Equal(source.Id, persistedNav.CategoryId);
        Assert.Equal(nav.Version, persistedNav.Version);
    }

    [PostgresFact]
    public async Task Delete_rolls_back_child_and_default_updates_when_version_check_fails()
    {
        using var admin = await App.AdministratorAsync();
        var source = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "來源" });
        var child = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "子分類", ParentId = source.Id });
        var settings = await admin.Settings.GetAsync();
        settings.DefaultCategoryId = source.Id;
        await admin.Settings.SaveAsync(settings);
        var staleVersion = source.Version;
        source.Name = "來源已更新";
        var current = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, source);

        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Taxonomy.DeleteAsync(TaxonomyKind.Category, source.Id!.Value, staleVersion));

        await using var db = await App.CreateContextAsync();
        var persistedSource = await db.Categories.SingleAsync(x => x.Id == source.Id);
        Assert.Equal(current.Version, persistedSource.Version);
        Assert.Equal("來源已更新", persistedSource.Name);
        Assert.Equal(source.Id, (await db.Categories.SingleAsync(x => x.Id == child.Id)).ParentId);
        Assert.Equal(source.Id, (await db.Settings.SingleAsync(x => x.Id == 1)).DefaultCategoryId);
    }

    [PostgresFact]
    public async Task Tag_conversion_rolls_back_navigation_and_defaults_when_version_check_fails()
    {
        using var admin = await App.AdministratorAsync();
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "旅行" });
        var post = NewContent("旅行文章");
        post.TagIds = [tag.Id!.Value];
        var saved = await admin.Content.SaveAsync(post);
        var settings = await admin.Settings.GetAsync();
        settings.DefaultTagId = tag.Id;
        await admin.Settings.SaveAsync(settings);
        var navigation = admin.Services.GetRequiredService<NavigationService>();
        var nav = await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "旅行", TagId = tag.Id });
        var staleVersion = tag.Version;
        tag.Name = "旅行已更新";
        var current = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, tag);

        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Taxonomy.ConvertTagToCategoryAsync(tag.Id!.Value, staleVersion));

        await using var db = await App.CreateContextAsync();
        var persistedTag = await db.Tags.SingleAsync(x => x.Id == tag.Id);
        Assert.Equal(current.Version, persistedTag.Version);
        Assert.Equal("旅行已更新", persistedTag.Name);
        Assert.False(await db.Categories.AnyAsync(x => x.Slug == tag.Slug));
        Assert.Equal(tag.Id, (await db.Settings.SingleAsync(x => x.Id == 1)).DefaultTagId);
        var persistedNav = await db.SiteNavItems.SingleAsync(x => x.Id == nav.Id);
        Assert.Equal(tag.Id, persistedNav.TagId);
        Assert.Null(persistedNav.CategoryId);
        Assert.Equal(nav.Version, persistedNav.Version);
        Assert.True(await db.Contents.Where(x => x.Id == saved.Id).SelectMany(x => x.Tags).AnyAsync(x => x.Id == tag.Id));
    }
}
