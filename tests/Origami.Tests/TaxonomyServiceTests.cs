using Microsoft.EntityFrameworkCore;
using Origami.Web.Models;
using Origami.Web.Services;

namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class TaxonomyServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
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
            var published = (await visitor.Content.GetPublishedAsync(ContentKind.Post, post.Slug))!;
            Assert.Equal(new[] { "Keep me" }, kind == TaxonomyKind.Category ? published.Categories : published.Tags);
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
}
