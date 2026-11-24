namespace Origami.Tests;

/// <summary>內容列表的排序、每頁筆數、內容類型篩選與排程頁籤，以及批次編輯的擴充動作。</summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentListExpansionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Admin_list_supports_sorting_paging_type_filter_and_scheduled_tab()
    {
        using var admin = await App.AdministratorAsync();
        var types = admin.Services.GetRequiredService<ContentTypeService>();
        var type = await types.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "review-type" });
        await admin.Content.SaveAsync(NewContent("Alpha 文章", status: ContentStatus.Published));
        await admin.Content.SaveAsync(NewContent("Beta 文章", status: ContentStatus.Published));
        var review = NewContent("Review item", status: ContentStatus.Published);
        review.ContentTypeId = type.Id;
        await admin.Content.SaveAsync(review);
        var scheduled = NewContent("排程文章");
        scheduled.PublishAt = DateTimeOffset.UtcNow.AddDays(3);
        await admin.Content.SaveAsync(scheduled);

        // 預設依最後更新倒序；每頁筆數生效並回報總頁數。
        var latest = await admin.Content.ListAdminAsync(ContentKind.Post);
        Assert.Equal("排程文章", latest.Items[0].Title);
        var paged = await admin.Content.ListAdminAsync(ContentKind.Post, 1, 2);
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal(2, paged.TotalPages);

        // 依標題排序。
        var byTitle = await admin.Content.ListAdminAsync(ContentKind.Post, sort: "title");
        Assert.Equal(["Alpha 文章", "Beta 文章", "Review item", "排程文章"], byTitle.Items.Select(x => x.Title).ToList());

        var now = DateTimeOffset.UtcNow;
        await using (var db = await App.CreateContextAsync())
        {
            var rows = await db.Contents.ToListAsync();
            var alpha = rows.Single(x => x.Title == "Alpha 文章");
            var beta = rows.Single(x => x.Title == "Beta 文章");
            var reviewRow = rows.Single(x => x.Title == "Review item");
            alpha.PublishedAt = now.AddDays(-3);
            beta.PublishedAt = now.AddDays(-2);
            reviewRow.PublishedAt = now.AddDays(-1);
            db.ContentViews.AddRange(
                new ContentView { ContentId = alpha.Id, Date = DateOnly.FromDateTime(now.UtcDateTime), Count = 10 },
                new ContentView { ContentId = beta.Id, Date = DateOnly.FromDateTime(now.UtcDateTime), Count = 30 },
                new ContentView { ContentId = reviewRow.Id, Date = DateOnly.FromDateTime(now.UtcDateTime), Count = 20 });
            await db.SaveChangesAsync();
        }
        var byViews = await admin.Content.ListAdminAsync(ContentKind.Post, sort: "views");
        Assert.Equal(["Beta 文章", "Review item", "Alpha 文章", "排程文章"], byViews.Items.Select(x => x.Title).ToList());
        var byPublished = await admin.Content.ListAdminAsync(ContentKind.Post, sort: "published");
        Assert.Equal(["Review item", "Beta 文章", "Alpha 文章", "排程文章"], byPublished.Items.Select(x => x.Title).ToList());

        // 內容類型篩選：自訂類型只列該類型；builtin 只列內建文章。
        var typed = await admin.Content.ListAdminAsync(ContentKind.Post, typeFilter: type.Id.ToString());
        Assert.Equal("Review item", Assert.Single(typed.Items).Title);
        var builtin = await admin.Content.ListAdminAsync(ContentKind.Post, typeFilter: "builtin");
        Assert.DoesNotContain(builtin.Items, x => x.Title == "Review item");
        Assert.Equal(3, builtin.Total);
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.ListAdminAsync(ContentKind.Post, typeFilter: "not-a-guid"));

        // 排程頁籤只列未來發布的草稿。
        var scheduledOnly = await admin.Content.ListAdminAsync(ContentKind.Post, scheduledOnly: true);
        Assert.Equal("排程文章", Assert.Single(scheduledOnly.Items).Title);
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.ListAdminAsync(ContentKind.Post, sort: "random"));
    }

    [PostgresFact]
    public async Task Bulk_edit_changes_status_removes_taxonomies_and_toggles_sticky()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "生活" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "週記" });
        var firstInput = NewContent("批次一", status: ContentStatus.Published);
        firstInput.CategoryIds = [category.Id!.Value];
        firstInput.TagIds = [tag.Id!.Value];
        var first = await admin.Content.SaveAsync(firstInput);
        var secondInput = NewContent("批次二");
        secondInput.CategoryIds = [category.Id.Value];
        secondInput.TagIds = [tag.Id.Value];
        var second = await admin.Content.SaveAsync(secondInput);

        async Task<List<BulkItemResult>> Run(BulkAction action, BulkEditOptions? options, params Guid[] ids)
        {
            var versions = new List<(Guid, Guid)>();
            foreach (var id in ids) versions.Add((id, (await admin.Content.GetEditAsync(id))!.Version));
            return await admin.Content.BulkAsync(action, versions, options: options);
        }

        Assert.All(await Run(BulkAction.Sticky, null, first.Id!.Value, second.Id!.Value), result => Assert.True(result.Succeeded, result.Message));
        Assert.True((await admin.Content.GetEditAsync(first.Id.Value))!.IsSticky);

        // 移除分類與標籤：只動指定的那一個，其餘指派保留。
        Assert.All(await Run(BulkAction.RemoveCategory, new BulkEditOptions(CategoryId: category.Id), first.Id.Value, second.Id.Value), result => Assert.True(result.Succeeded, result.Message));
        Assert.All(await Run(BulkAction.RemoveTag, new BulkEditOptions(TagId: tag.Id), first.Id.Value), result => Assert.True(result.Succeeded, result.Message));
        var firstAfter = (await admin.Content.GetEditAsync(first.Id.Value))!;
        Assert.Empty(firstAfter.CategoryIds);
        Assert.Empty(firstAfter.TagIds);
        Assert.Equal([tag.Id!.Value], (await admin.Content.GetEditAsync(second.Id.Value))!.TagIds);

        // 批次發布：草稿變已發布，已發布者維持；撤回後變草稿。
        Assert.All(await Run(BulkAction.SetStatus, new BulkEditOptions(Status: ContentStatus.Published), first.Id.Value, second.Id.Value), result => Assert.True(result.Succeeded, result.Message));
        Assert.Equal(ContentStatus.Published, (await admin.Content.GetEditAsync(second.Id.Value))!.Status);
        // 未指定目標狀態時逐筆回報失敗，不影響內容。
        var missing = await admin.Content.BulkAsync(BulkAction.SetStatus, [(first.Id.Value, firstAfter.Version)]);
        Assert.False(missing[0].Succeeded);
        await admin.Content.TrashAsync(second.Id.Value, (await admin.Content.GetEditAsync(second.Id.Value))!.Version, confirmed: true);
        var mixed = await admin.Content.BulkAsync(BulkAction.SetStatus,
            [(first.Id.Value, (await admin.Content.GetEditAsync(first.Id.Value))!.Version),
             (second.Id.Value, (await admin.Content.GetEditAsync(second.Id.Value))!.Version)], options: new BulkEditOptions(Status: ContentStatus.Draft));
        Assert.True(mixed[0].Succeeded, mixed[0].Message);
        Assert.False(mixed[1].Succeeded);
        Assert.Contains("回收桶", mixed[1].Message, StringComparison.Ordinal);
        Assert.Equal(ContentStatus.Draft, (await admin.Content.GetEditAsync(first.Id.Value))!.Status);

        Assert.All(await Run(BulkAction.Unsticky, null, first.Id.Value), result => Assert.True(result.Succeeded, result.Message));
        Assert.False((await admin.Content.GetEditAsync(first.Id.Value))!.IsSticky);
    }

    [PostgresFact]
    public async Task Bulk_set_status_rejects_stale_version_for_unchanged_status()
    {
        using var admin = await App.AdministratorAsync();
        var stale = await admin.Content.SaveAsync(NewContent("同狀態批次版本衝突", status: ContentStatus.Draft));
        Guid currentVersion;
        await using (var db = await App.CreateContextAsync())
        {
            var current = await db.Contents.SingleAsync(x => x.Id == stale.Id);
            current.Title = "另一分頁已更新";
            currentVersion = Guid.NewGuid();
            current.Version = currentVersion;
            await db.SaveChangesAsync();
        }
        Assert.NotEqual(stale.Version, currentVersion);

        var result = Assert.Single(await admin.Content.BulkAsync(BulkAction.SetStatus,
            [(stale.Id!.Value, stale.Version)], options: new BulkEditOptions(Status: ContentStatus.Draft)));

        Assert.False(result.Succeeded);
        Assert.Contains("另一個分頁", result.Message, StringComparison.Ordinal);
        await using var verify = await App.CreateContextAsync();
        var persisted = await verify.Contents.AsNoTracking().SingleAsync(x => x.Id == stale.Id);
        Assert.Equal("另一分頁已更新", persisted.Title);
        Assert.Equal(currentVersion, persisted.Version);
    }

    [PostgresFact]
    public async Task Bulk_edit_is_limited_to_editors()
    {
        using var admin = await App.AdministratorAsync();
        var post = await admin.Content.SaveAsync(NewContent("作者不可批次", status: ContentStatus.Published));
        var author = await CreateMemberAsync(SiteRole.Author);
        using var authorActor = await App.UserAsync(author.Id);
        await Assert.ThrowsAnyAsync<Exception>(() => authorActor.Content.BulkAsync(BulkAction.Unsticky, [(post.Id!.Value, post.Version)]));
    }
}
