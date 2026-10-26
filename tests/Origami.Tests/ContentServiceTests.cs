namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ContentServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task The_server_decides_the_content_kind_for_new_and_existing_content()
    {
        using var admin = await App.AdministratorAsync();
        // Creating states the kind explicitly; the service validates and applies it.
        var page = await admin.Content.CreateAsync(ContentKind.Page, NewContent("About"));
        Assert.Equal(ContentKind.Page, page.Kind);
        // An update keeps the stored kind even if the submitted model disagrees.
        var edit = await admin.Content.GetEditAsync(page.Id!.Value);
        edit.Kind = ContentKind.Post;
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(edit));
        Assert.Equal(ContentKind.Page, (await admin.Content.GetEditAsync(page.Id!.Value)).Kind);
        // An update cannot smuggle a different kind through the create path either.
        var post = await admin.Content.CreateAsync(ContentKind.Post, NewContent("Journey"));
        var postEdit = await admin.Content.GetEditAsync(post.Id!.Value);
        postEdit.Kind = ContentKind.Page;
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(postEdit));
        Assert.Equal(ContentKind.Post, (await admin.Content.GetEditAsync(post.Id!.Value)).Kind);
        // Creating with a kind outside the defined set is refused.
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.CreateAsync((ContentKind)99, NewContent("Invalid")));
        await using var db = await App.CreateContextAsync();
        Assert.Equal(2, await db.Contents.CountAsync());
    }

    [PostgresFact]
    public async Task Repeated_submission_of_the_same_create_is_rejected_by_the_unique_slug()
    {
        using var admin = await App.AdministratorAsync();
        var first = await admin.Content.CreateAsync(ContentKind.Post, NewContent("Only once"));
        // A double submit carries the same slug, so the database refuses the second row.
        var duplicate = await Assert.ThrowsAsync<CmsException>(
            () => admin.Content.CreateAsync(ContentKind.Post, NewContent("Only once")));
        Assert.Contains("代稱", duplicate.Message, StringComparison.Ordinal);
        await using var db = await App.CreateContextAsync();
        Assert.Single(await db.Contents.ToListAsync());
        Assert.Equal(first.Slug, (await db.Contents.SingleAsync()).Slug);
    }

    [PostgresTheory]
    [InlineData(TaxonomyKind.Category)]
    [InlineData(TaxonomyKind.Tag)]
    public async Task Overlapping_saves_past_the_version_check_report_a_conflict_not_a_duplicate_slug(TaxonomyKind kind)
    {
        using var actor = await App.AdministratorAsync();
        var taxonomy = await actor.Taxonomy.SaveAsync(kind, new() { Name = "Race" });
        var post = await actor.Content.SaveAsync(NewContent("Race post"));
        var left = await actor.Content.GetEditAsync(post.Id!.Value);
        var right = await actor.Content.GetEditAsync(post.Id.Value);
        left.Title = "Race winner";
        left.Markdown = "Winner body";
        right.Title = "Race loser";
        right.Markdown = "Loser body";
        (kind == TaxonomyKind.Category ? left.CategoryIds : left.TagIds).Add(taxonomy.Id!.Value);
        (kind == TaxonomyKind.Category ? right.CategoryIds : right.TagIds).Add(taxonomy.Id.Value);

        var adminAccess = actor.Services.GetRequiredService<AdminAccess>();
        var gateA = new SaveGate();
        var gateB = new SaveGate();
        var writerA = new ContentService(new InterceptingContextFactory(App.ConnectionString, gateA), adminAccess, TimeProvider.System);
        var writerB = new ContentService(new InterceptingContextFactory(App.ConnectionString, gateB), adminAccess, TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var saveA = writerA.SaveAsync(left, timeout.Token);
        var saveB = writerB.SaveAsync(right, timeout.Token);
        ContentEdit winner;
        try
        {
            // Both saves must already be past the version check before either write commits.
            await Task.WhenAll(gateA.Ready.Task, gateB.Ready.Task).WaitAsync(TimeSpan.FromSeconds(15), timeout.Token);
            gateA.Release.TrySetResult();
            winner = await saveA;
            gateB.Release.TrySetResult();
            var loserError = await Assert.ThrowsAsync<CmsException>(() => saveB);
            Assert.Equal("資料已被另一個分頁更新，請重新載入後再編輯。", loserError.Message);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, gateB.SqlState);
            Assert.Equal(kind == TaxonomyKind.Category ? "PK_ContentCategories" : "PK_ContentTags", gateB.Constraint);
        }
        finally
        {
            timeout.Cancel();
            gateA.Release.TrySetResult();
            gateB.Release.TrySetResult();
            await Task.WhenAll(ObserveAsync(saveA), ObserveAsync(saveB)).WaitAsync(TimeSpan.FromSeconds(15));
        }

        using var fresh = await App.AdministratorAsync();
        var persisted = await fresh.Content.GetEditAsync(post.Id!.Value);
        Assert.Equal("Race winner", persisted.Title);
        Assert.Equal("Winner body", persisted.Markdown);
        Assert.Equal(winner.Version, persisted.Version);
        var associationIds = kind == TaxonomyKind.Category ? persisted.CategoryIds : persisted.TagIds;
        Assert.Equal(taxonomy.Id, Assert.Single(associationIds));
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task; }
        catch { /* cleanup-only observation; the original failure already failed the test */ }
    }

    [PostgresTheory]
    [InlineData(ContentKind.Post)]
    [InlineData(ContentKind.Page)]
    public async Task Draft_publish_edit_withdraw_republish_delete_is_visible_only_when_published(ContentKind kind)
    {
        using var admin = await App.AdministratorAsync();
        using var visitor = App.Anonymous();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var edit = await admin.Content.SaveAsync(NewContent("First title", kind));
        var id = edit.Id!.Value;
        Assert.Null(await visitor.Content.GetPublishedAsync(kind, edit.Slug!));
        Assert.Empty((await visitor.Content.ListPublishedAsync()).Items);
        Assert.Empty(await visitor.Content.NavigationAsync());
        Assert.Single((await admin.Content.ListAdminAsync(kind)).Items);
        await using (var db = await App.CreateContextAsync())
        {
            var row = await db.Contents.SingleAsync();
            Assert.Null(row.PublishedAt);
            Assert.Equal(TimeSpan.Zero, row.CreatedAt.Offset);
            Assert.Equal(TimeSpan.Zero, row.UpdatedAt.Offset);
            Assert.InRange(row.CreatedAt, before, DateTimeOffset.UtcNow);
            Assert.True(row.UpdatedAt >= row.CreatedAt);
        }

        edit.Status = ContentStatus.Published;
        edit = await admin.Content.SaveAsync(edit);
        var published = Assert.IsType<PublicContent>(await visitor.Content.GetPublishedAsync(kind, edit.Slug!));
        var publishedAt = published.PublishedAt!.Value;
        Assert.Equal(TimeSpan.Zero, publishedAt.Offset);
        Assert.InRange(publishedAt, before, DateTimeOffset.UtcNow);
        var otherKind = kind == ContentKind.Post ? ContentKind.Page : ContentKind.Post;
        Assert.Null(await visitor.Content.GetPublishedAsync(otherKind, edit.Slug!));
        Assert.Equal(kind == ContentKind.Post ? 1 : 0, (await visitor.Content.ListPublishedAsync()).Total);
        Assert.Equal(kind == ContentKind.Page ? 1 : 0, (await visitor.Content.NavigationAsync()).Count);

        var stableSlug = edit.Slug!;
        edit.Title = "Changed title";
        edit.Markdown = "An immediately visible edit";
        edit = await admin.Content.SaveAsync(edit);
        Assert.Equal(stableSlug, edit.Slug);
        published = Assert.IsType<PublicContent>(await visitor.Content.GetPublishedAsync(kind, stableSlug));
        Assert.Equal("Changed title", published.Title);
        Assert.Equal(edit.Markdown, published.Markdown);
        Assert.Equal(publishedAt, published.PublishedAt);

        edit.Status = ContentStatus.Draft;
        edit = await admin.Content.SaveAsync(edit);
        Assert.Null(await visitor.Content.GetPublishedAsync(kind, stableSlug));
        Assert.Empty((await visitor.Content.ListPublishedAsync()).Items);
        Assert.Empty(await visitor.Content.NavigationAsync());
        await using (var db = await App.CreateContextAsync())
            Assert.Equal(publishedAt, (await db.Contents.SingleAsync()).PublishedAt);

        edit.Status = ContentStatus.Published;
        edit = await admin.Content.SaveAsync(edit);
        Assert.Equal(publishedAt, (await visitor.Content.GetPublishedAsync(kind, stableSlug))!.PublishedAt);
        await admin.Content.DeleteAsync(id, edit.Version, confirmed: true);
        Assert.Null(await visitor.Content.GetPublishedAsync(kind, stableSlug));
        Assert.Empty((await visitor.Content.ListPublishedAsync()).Items);
        Assert.Empty(await visitor.Content.NavigationAsync());
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.GetEditAsync(id));
        await using var finalDb = await App.CreateContextAsync();
        Assert.Empty(await finalDb.Contents.ToListAsync());
    }

    [PostgresFact]
    public async Task Slugs_are_normalized_globally_unique_and_change_only_when_explicitly_edited()
    {
        using var admin = await App.AdministratorAsync();
        var first = NewContent("ＡＢＣ My Title");
        first.Slug = "  SAME__Slug  ";
        first = await admin.Content.SaveAsync(first);
        Assert.Equal("same-slug", first.Slug);
        var duplicate = NewContent("Another title", ContentKind.Page);
        duplicate.Slug = "ＳＡＭＥ / SLUG";
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(duplicate));
        first.Title = "A completely different title";
        first = await admin.Content.SaveAsync(first);
        Assert.Equal("same-slug", first.Slug);
        first.Slug = "Explicit NEW URL";
        first = await admin.Content.SaveAsync(first);
        Assert.Equal("explicit-new-url", first.Slug);
        await using var db = await App.CreateContextAsync();
        Assert.Single(await db.Contents.ToListAsync());
    }

    [PostgresFact]
    public async Task Duplicate_slug_rolls_back_content_and_both_sets_of_associations()
    {
        using var admin = await App.AdministratorAsync();
        var c1 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "Old category" });
        var c2 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "New category" });
        var t1 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new() { Name = "Old tag" });
        var t2 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new() { Name = "New tag" });
        var original = NewContent("Original");
        original.CategoryIds = [c1.Id!.Value];
        original.TagIds = [t1.Id!.Value];
        original = await admin.Content.SaveAsync(original);
        var occupied = await admin.Content.SaveAsync(NewContent("Occupied"));
        var edit = await admin.Content.GetEditAsync(original.Id!.Value);
        edit.Title = "Must roll back";
        edit.Markdown = "Must also roll back";
        edit.Slug = occupied.Slug;
        edit.Status = ContentStatus.Published;
        edit.CategoryIds = [c2.Id!.Value];
        edit.TagIds = [t2.Id!.Value];
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(edit));

        var after = await admin.Content.GetEditAsync(original.Id.Value);
        Assert.Equal(original.Title, after.Title);
        Assert.Equal(original.Markdown, after.Markdown);
        Assert.Equal(original.Slug, after.Slug);
        Assert.Equal(original.Version, after.Version);
        Assert.Equal(ContentStatus.Draft, after.Status);
        Assert.Equal(original.CategoryIds, after.CategoryIds);
        Assert.Equal(original.TagIds, after.TagIds);
        await using var db = await App.CreateContextAsync();
        Assert.Null((await db.Contents.SingleAsync(x => x.Id == original.Id)).PublishedAt);
        Assert.Equal(2, await db.Contents.CountAsync());
    }

    [PostgresFact]
    public async Task Two_actor_stale_updates_and_deletes_cannot_overwrite_the_winner()
    {
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync();
        var saved = await first.Content.SaveAsync(NewContent());
        var left = await first.Content.GetEditAsync(saved.Id!.Value);
        var right = await second.Content.GetEditAsync(saved.Id.Value);
        left.Title = "Winner";
        left = await first.Content.SaveAsync(left);
        Assert.NotEqual(right.Version, left.Version);
        right.Title = "Stale loser";
        await Assert.ThrowsAsync<CmsException>(() => second.Content.SaveAsync(right));
        await Assert.ThrowsAsync<CmsException>(() => second.Content.DeleteAsync(saved.Id.Value, right.Version, true));
        Assert.Equal("Winner", (await second.Content.GetEditAsync(saved.Id.Value)).Title);
        await second.Content.DeleteAsync(saved.Id.Value, left.Version, true);
        await Assert.ThrowsAsync<CmsException>(() => first.Content.SaveAsync(left));
        await Assert.ThrowsAsync<CmsException>(() => first.Content.DeleteAsync(saved.Id.Value, left.Version, true));
    }

    [PostgresFact]
    public async Task Delete_requires_confirmation_and_removes_joins_but_not_taxonomies()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "Category" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new() { Name = "Tag" });
        var post = NewContent();
        post.CategoryIds = [category.Id!.Value];
        post.TagIds = [tag.Id!.Value];
        post = await admin.Content.SaveAsync(post);
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.DeleteAsync(post.Id!.Value, post.Version, false));
        Assert.NotNull(await admin.Content.GetEditAsync(post.Id!.Value));
        await admin.Content.DeleteAsync(post.Id.Value, post.Version, true);
        await using var db = await App.CreateContextAsync();
        Assert.Empty((await db.Categories.Include(x => x.Contents).SingleAsync()).Contents);
        Assert.Empty((await db.Tags.Include(x => x.Contents).SingleAsync()).Contents);
        Assert.Empty(await db.Contents.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("kind")]
    [InlineData("status")]
    [InlineData("blank-title")]
    [InlineData("long-title")]
    [InlineData("long-slug")]
    [InlineData("separator-slug")]
    [InlineData("long-body")]
    [InlineData("missing-category")]
    [InlineData("missing-tag")]
    [InlineData("too-many-categories")]
    [InlineData("too-many-tags")]
    [InlineData("null-categories")]
    [InlineData("null-tags")]
    [InlineData("page-category")]
    [InlineData("page-tag")]
    public async Task Invalid_input_is_rejected_without_creating_content(string scenario)
    {
        using var admin = await App.AdministratorAsync();
        var input = NewContent();
        switch (scenario)
        {
            case "kind": input.Kind = (ContentKind)99; break;
            case "status": input.Status = (ContentStatus)99; break;
            case "blank-title": input.Title = " \t"; break;
            case "long-title": input.Title = new string('x', Limits.Title + 1); break;
            case "long-slug": input.Slug = new string('x', Limits.Slug + 1); break;
            case "separator-slug": input.Slug = "///"; break;
            case "long-body": input.Markdown = new string('x', Limits.Body + 1); break;
            case "missing-category": input.CategoryIds = [Guid.NewGuid()]; break;
            case "missing-tag": input.TagIds = [Guid.NewGuid()]; break;
            case "too-many-categories": input.CategoryIds = Enumerable.Range(0, Limits.MaxTaxonomiesPerKind + 1).Select(_ => Guid.NewGuid()).ToList(); break;
            case "too-many-tags": input.TagIds = Enumerable.Range(0, Limits.MaxTaxonomiesPerKind + 1).Select(_ => Guid.NewGuid()).ToList(); break;
            case "null-categories": input.CategoryIds = null!; break;
            case "null-tags": input.TagIds = null!; break;
            case "page-category": input.Kind = ContentKind.Page; input.CategoryIds = [Guid.NewGuid()]; break;
            case "page-tag": input.Kind = ContentKind.Page; input.TagIds = [Guid.NewGuid()]; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(input));
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.Contents.ToListAsync());
    }

    [PostgresFact]
    public async Task Existing_kind_cannot_change_and_existing_slug_cannot_be_cleared()
    {
        using var admin = await App.AdministratorAsync();
        var saved = await admin.Content.SaveAsync(NewContent());
        var edit = await admin.Content.GetEditAsync(saved.Id!.Value);
        edit.Kind = ContentKind.Page;
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(edit));
        edit.Kind = ContentKind.Post;
        edit.Slug = " ";
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(edit));
        var after = await admin.Content.GetEditAsync(saved.Id.Value);
        Assert.Equal(saved.Kind, after.Kind);
        Assert.Equal(saved.Slug, after.Slug);
        Assert.Equal(saved.Version, after.Version);
    }

    [PostgresFact]
    public async Task Maximum_lengths_and_association_counts_are_persisted()
    {
        using var admin = await App.AdministratorAsync();
        await using var db = await App.CreateContextAsync();
        var categories = Enumerable.Range(0, Limits.MaxTaxonomiesPerKind)
            .Select(i => new Category { Name = "Category " + i, Slug = "category-" + i }).ToList();
        var tags = Enumerable.Range(0, Limits.MaxTaxonomiesPerKind)
            .Select(i => new Tag { Name = "Tag " + i, Slug = "tag-" + i }).ToList();
        db.AddRange(categories);
        db.AddRange(tags);
        await db.SaveChangesAsync();
        var input = NewContent(new string('t', Limits.Title));
        input.Slug = new string('s', Limits.Slug);
        input.Markdown = new string('b', Limits.Body);
        input.CategoryIds = categories.Select(x => x.Id).ToList();
        input.TagIds = tags.Select(x => x.Id).ToList();
        var saved = await admin.Content.SaveAsync(input);
        var loaded = await admin.Content.GetEditAsync(saved.Id!.Value);
        Assert.Equal(input.Title, loaded.Title);
        Assert.Equal(input.Slug, loaded.Slug);
        Assert.Equal(input.Markdown, loaded.Markdown);
        Assert.Equal(Limits.MaxTaxonomiesPerKind, loaded.CategoryIds.Count);
        Assert.Equal(Limits.MaxTaxonomiesPerKind, loaded.TagIds.Count);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_queries_fetch_the_body_once_without_cross_joining_taxonomies(bool published)
    {
        using var admin = await App.AdministratorAsync();
        await using var db = await App.CreateContextAsync();
        var categories = Enumerable.Range(0, Limits.MaxTaxonomiesPerKind)
            .Select(i => new Category { Name = "Category " + i, Slug = "category-" + i }).ToList();
        var tags = Enumerable.Range(0, Limits.MaxTaxonomiesPerKind)
            .Select(i => new Tag { Name = "Tag " + i, Slug = "tag-" + i }).ToList();
        db.AddRange(categories);
        db.AddRange(tags);
        await db.SaveChangesAsync();
        var input = NewContent("Bounded detail", status: ContentStatus.Published);
        input.Markdown = new string('x', Limits.Body);
        input.CategoryIds = categories.Select(x => x.Id).ToList();
        input.TagIds = tags.Select(x => x.Id).ToList();
        var saved = await admin.Content.SaveAsync(input);
        var recorder = new QueryRecorder();
        var service = new ContentService(new InterceptingContextFactory(App.ConnectionString, recorder),
            admin.Services.GetRequiredService<AdminAccess>(), TimeProvider.System);

        if (published)
        {
            var result = Assert.IsType<PublicContent>(await service.GetPublishedAsync(ContentKind.Post, saved.Slug!));
            Assert.Equal(input.Markdown, result.Markdown);
            Assert.Equal(Limits.MaxTaxonomiesPerKind, result.Categories.Count);
            Assert.Equal(Limits.MaxTaxonomiesPerKind, result.Tags.Count);
        }
        else
        {
            var result = await service.GetEditAsync(saved.Id!.Value);
            Assert.Equal(input.Markdown, result.Markdown);
            Assert.Equal(Limits.MaxTaxonomiesPerKind, result.CategoryIds.Count);
            Assert.Equal(Limits.MaxTaxonomiesPerKind, result.TagIds.Count);
        }

        Assert.Equal(3, recorder.Commands.Count);
        Assert.Single(recorder.Commands, sql => sql.Contains("\"Markdown\"", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Commands, sql => sql.Contains("\"ContentCategories\"", StringComparison.Ordinal)
            && sql.Contains("\"ContentTags\"", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task Multiple_associations_are_deduplicated_and_sorted_on_the_public_projection()
    {
        using var admin = await App.AdministratorAsync();
        using var visitor = App.Anonymous();
        var c1 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "Zebra" });
        var c2 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "Alpha" });
        var t1 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new() { Name = "Zebra" });
        var t2 = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new() { Name = "Alpha" });
        var input = NewContent(status: ContentStatus.Published);
        input.CategoryIds = [c1.Id!.Value, c2.Id!.Value, c1.Id.Value];
        input.TagIds = [t1.Id!.Value, t2.Id!.Value, t1.Id.Value];
        var saved = await admin.Content.SaveAsync(input);
        var loaded = await admin.Content.GetEditAsync(saved.Id!.Value);
        Assert.Equal(2, loaded.CategoryIds.Count);
        Assert.Equal(2, loaded.TagIds.Count);
        var published = (await visitor.Content.GetPublishedAsync(ContentKind.Post, saved.Slug!))!;
        Assert.Equal([new NavigationItem("Alpha", "alpha"), new NavigationItem("Zebra", "zebra")], published.Categories);
        Assert.Equal([new NavigationItem("Alpha", "alpha"), new NavigationItem("Zebra", "zebra")], published.Tags);
    }

    [PostgresFact]
    public async Task Published_and_admin_pagination_are_stable_for_tied_timestamps()
    {
        using var admin = await App.AdministratorAsync();
        using var visitor = App.Anonymous();
        var posts = new List<ContentEdit>();
        for (var i = 0; i < 7; i++) posts.Add(await admin.Content.SaveAsync(NewContent("Post " + i, status: ContentStatus.Published)));
        var draft = await admin.Content.SaveAsync(NewContent("Excluded draft"));
        await admin.Content.SaveAsync(NewContent("Excluded page", ContentKind.Page, ContentStatus.Published));
        var timestamp = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await using (var db = await App.CreateContextAsync())
        {
            await db.Contents.ExecuteUpdateAsync(set => set.SetProperty(x => x.UpdatedAt, timestamp));
            await db.Contents.Where(x => x.Status == ContentStatus.Published)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.PublishedAt, (DateTimeOffset?)timestamp));
        }
        var expected = posts.Select(x => x.Id!.Value).OrderByDescending(x => x.ToString("N"), StringComparer.Ordinal).ToArray();
        var actual = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await visitor.Content.ListPublishedAsync(page, 3);
            Assert.Equal(7, result.Total);
            Assert.Equal(3, result.TotalPages);
            Assert.Equal(page, result.Page);
            Assert.Equal(3, result.PageSize);
            actual.AddRange(result.Items.Select(x => x.Id));
            Assert.Equal(result.Items.Select(x => x.Id), (await visitor.Content.ListPublishedAsync(page, 3)).Items.Select(x => x.Id));
        }
        Assert.Equal(expected, actual);
        Assert.Equal(7, actual.Distinct().Count());
        var beyond = await visitor.Content.ListPublishedAsync(4, 3);
        Assert.Empty(beyond.Items);
        Assert.Equal(7, beyond.Total);
        var adminExpected = posts.Append(draft).Select(x => x.Id!.Value)
            .OrderByDescending(x => x.ToString("N"), StringComparer.Ordinal).ToArray();
        var adminActual = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await admin.Content.ListAdminAsync(ContentKind.Post, page, 3);
            Assert.Equal(8, result.Total);
            adminActual.AddRange(result.Items.Select(x => x.Id));
        }
        Assert.Equal(adminExpected, adminActual);
        Assert.Single((await admin.Content.ListAdminAsync(ContentKind.Page)).Items);
        Assert.Empty((await admin.Content.ListAdminAsync(ContentKind.Post, 4, 3)).Items);
    }

    [PostgresFact]
    public async Task Empty_pagination_defaults_and_invalid_service_parameters_are_enforced()
    {
        using var admin = await App.AdministratorAsync();
        using var visitor = App.Anonymous();
        var empty = await visitor.Content.ListPublishedAsync();
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.Total);
        Assert.Equal(1, empty.TotalPages);
        Assert.Equal(Limits.PageSize, empty.PageSize);
        Assert.Empty((await visitor.Content.ListPublishedAsync(1, Limits.MaxPageSize)).Items);
        foreach (var (page, size) in new[] { (0, 10), (1, 0), (1, Limits.MaxPageSize + 1), (int.MaxValue, 2) })
        {
            await Assert.ThrowsAsync<CmsException>(() => visitor.Content.ListPublishedAsync(page, size));
            await Assert.ThrowsAsync<CmsException>(() => admin.Content.ListAdminAsync(ContentKind.Post, page, size));
        }
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.ListAdminAsync((ContentKind)99));
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.GetEditAsync(Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task Published_timestamp_takes_precedence_over_updated_timestamp_and_identifier()
    {
        using var admin = await App.AdministratorAsync();
        using var visitor = App.Anonymous();
        var first = await admin.Content.SaveAsync(NewContent("First", status: ContentStatus.Published));
        var second = await admin.Content.SaveAsync(NewContent("Second", status: ContentStatus.Published));
        var orderedIds = new[] { first.Id!.Value, second.Id!.Value }
            .OrderBy(x => x.ToString("N"), StringComparer.Ordinal).ToArray();
        var newer = orderedIds[0];
        var older = orderedIds[1];
        var timestamp = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var db = await App.CreateContextAsync())
        {
            await db.Contents.Where(x => x.Id == newer).ExecuteUpdateAsync(set => set
                .SetProperty(x => x.PublishedAt, (DateTimeOffset?)timestamp.AddDays(1))
                .SetProperty(x => x.UpdatedAt, timestamp));
            await db.Contents.Where(x => x.Id == older).ExecuteUpdateAsync(set => set
                .SetProperty(x => x.PublishedAt, (DateTimeOffset?)timestamp)
                .SetProperty(x => x.UpdatedAt, timestamp.AddDays(2)));
        }
        Assert.Equal(new[] { newer, older }, (await visitor.Content.ListPublishedAsync()).Items.Select(x => x.Id));
        Assert.Equal(new[] { older, newer }, (await admin.Content.ListAdminAsync(ContentKind.Post)).Items.Select(x => x.Id));
    }

    [PostgresFact]
    public async Task Navigation_contains_only_published_pages_in_title_order()
    {
        using var admin = await App.AdministratorAsync();
        using var visitor = App.Anonymous();
        await admin.Content.SaveAsync(NewContent("Zebra", ContentKind.Page, ContentStatus.Published));
        await admin.Content.SaveAsync(NewContent("Alpha", ContentKind.Page, ContentStatus.Published));
        await admin.Content.SaveAsync(NewContent("Hidden", ContentKind.Page));
        await admin.Content.SaveAsync(NewContent("Not a page", status: ContentStatus.Published));
        Assert.Equal(new[] { "Alpha", "Zebra" }, (await visitor.Content.NavigationAsync()).Select(x => x.Title));
    }

    [PostgresFact]
    public async Task Admin_status_filters_and_counts_are_scoped_authorized_and_paged()
    {
        using var admin = await App.AdministratorAsync();
        using var visitor = App.Anonymous();
        Assert.Equal(new ContentCounts(0, 0), await admin.Content.CountAdminAsync(ContentKind.Post));
        for (var i = 0; i < 3; i++) await admin.Content.SaveAsync(NewContent("Draft " + i));
        var published = await admin.Content.SaveAsync(NewContent("Public post", status: ContentStatus.Published));
        await admin.Content.SaveAsync(NewContent("Separate page", ContentKind.Page, ContentStatus.Published));
        Assert.Equal(new ContentCounts(3, 1), await admin.Content.CountAdminAsync(ContentKind.Post));
        Assert.Equal(new ContentCounts(0, 1), await admin.Content.CountAdminAsync(ContentKind.Page));
        var first = await admin.Content.ListAdminAsync(ContentKind.Post, pageSize: 2, status: ContentStatus.Draft);
        var second = await admin.Content.ListAdminAsync(ContentKind.Post, 2, 2, status: ContentStatus.Draft);
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.TotalPages);
        Assert.Single(second.Items);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count());
        Assert.All(first.Items.Concat(second.Items), x => Assert.Equal(ContentStatus.Draft, x.Status));
        Assert.Equal(published.Id, Assert.Single((await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Published)).Items).Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => visitor.Content.CountAdminAsync(ContentKind.Post));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => visitor.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Draft));
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.CountAdminAsync((ContentKind)99));
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.ListAdminAsync(ContentKind.Post, status: (ContentStatus)99));
    }

    [PostgresFact]
    public async Task Public_taxonomy_browsing_excludes_drafts_and_removes_withdrawn_content_immediately()
    {
        using var admin = await App.AdministratorAsync();
        using var visitor = App.Anonymous();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "旅行", Slug = "travel" });
        var privateCategory = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "Private topic" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new() { Name = "徒步", Slug = "walking" });
        var post = NewContent("Public journey", status: ContentStatus.Published);
        post.CategoryIds = [category.Id!.Value];
        post.TagIds = [tag.Id!.Value];
        post.Markdown = new string('a', 900);
        post = await admin.Content.SaveAsync(post);
        var draft = NewContent("Hidden journey");
        draft.CategoryIds = [category.Id.Value, privateCategory.Id!.Value];
        draft.TagIds = [tag.Id.Value];
        await admin.Content.SaveAsync(draft);
        var other = NewContent("Unrelated public post", status: ContentStatus.Published);
        other.CategoryIds = [category.Id.Value];
        await admin.Content.SaveAsync(other);
        await admin.Content.SaveAsync(NewContent("Standalone page", ContentKind.Page, ContentStatus.Published));

        Assert.Equal(new NavigationItem("旅行", "travel"), await visitor.Content.GetPublicTaxonomyAsync(TaxonomyKind.Category, "TRAVEL"));
        Assert.Null(await visitor.Content.GetPublicTaxonomyAsync(TaxonomyKind.Category, privateCategory.Slug));
        Assert.Null(await visitor.Content.GetPublicTaxonomyAsync(TaxonomyKind.Tag, "missing"));
        var popular = Assert.Single(await visitor.Content.PopularCategoriesAsync());
        Assert.Equal("travel", popular.Slug);
        Assert.Equal(2, popular.Count);
        var categoryPage = await visitor.Content.ListPublishedAsync(pageSize: 1, category: "travel");
        Assert.Equal(2, categoryPage.Total);
        Assert.Equal(2, categoryPage.TotalPages);
        var next = await visitor.Content.ListPublishedAsync(2, 1, category: "travel");
        Assert.NotEqual(Assert.Single(categoryPage.Items).Id, Assert.Single(next.Items).Id);
        var filtered = Assert.Single((await visitor.Content.ListPublishedAsync(category: "travel", tag: "walking")).Items);
        Assert.Equal(post.Id, filtered.Id);
        Assert.Equal(600, filtered.Excerpt.Length);
        await Assert.ThrowsAsync<CmsException>(() => visitor.Content.ListPublishedAsync(category: new string('x', Limits.Slug + 1)));

        post.Status = ContentStatus.Draft;
        post = await admin.Content.SaveAsync(post);
        Assert.Empty((await visitor.Content.ListPublishedAsync(tag: "walking")).Items);
        Assert.Null(await visitor.Content.GetPublicTaxonomyAsync(TaxonomyKind.Tag, "walking"));
        Assert.Equal(1, Assert.Single(await visitor.Content.PopularCategoriesAsync()).Count);
        await admin.Taxonomy.DeleteAsync(TaxonomyKind.Category, category.Id.Value, category.Version);
        Assert.Empty(await visitor.Content.PopularCategoriesAsync());
        Assert.Empty((await visitor.Content.ListPublishedAsync(category: "travel")).Items);
        Assert.Equal(1, (await visitor.Content.ListPublishedAsync()).Total);
    }

    private sealed class InterceptingContextFactory(string connectionString, IInterceptor interceptor) : IDbContextFactory<CmsDbContext>
    {
        public CmsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CmsDbContext>().UseNpgsql(connectionString, o => o.UseVector())
            .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .AddInterceptors(interceptor).Options);

        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class QueryRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? SqlState { get; private set; }
        public string? Constraint { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Ready.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Exception.InnerException is PostgresException postgres)
            {
                SqlState = postgres.SqlState;
                Constraint = postgres.ConstraintName;
            }
            return Task.CompletedTask;
        }
    }
}
