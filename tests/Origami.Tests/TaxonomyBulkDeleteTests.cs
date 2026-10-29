namespace Origami.Tests;

/// <summary>分類標籤批次刪除：逐筆版本檢查、逐筆回報、只移除關聯不刪文章、空批與超量拒絕。</summary>
[Collection(PostgresCollection.Name)]
public sealed class TaxonomyBulkDeleteTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Bulk_delete_reports_each_item_and_keeps_contents()
    {
        using var admin = await App.AdministratorAsync();
        var keep = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "保留" });
        var first = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "批次一" });
        var second = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "批次二" });

        // 文章同時指派三個分類；刪除其中兩個後文章必須完整存活。
        var post = NewContent("批次刪除下的文章", status: ContentStatus.Published);
        post.CategoryIds = [keep.Id!.Value, first.Id!.Value, second.Id!.Value];
        await admin.Content.SaveAsync(post);

        // 以過期版本刪除 second：該筆失敗，其余照常執行。
        var results = await admin.Taxonomy.BulkDeleteAsync(TaxonomyKind.Category,
        [
            (first.Id!.Value, first.Version),
            (second.Id!.Value, Guid.NewGuid()),
        ]);
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Succeeded);
        Assert.False(results[1].Succeeded);

        await using var db = await App.CreateContextAsync();
        Assert.False(await db.Categories.AsNoTracking().AnyAsync(x => x.Id == first.Id));
        Assert.True(await db.Categories.AsNoTracking().AnyAsync(x => x.Id == second.Id));
        var saved = await db.Contents.AsNoTracking().Include(x => x.Categories).SingleAsync(x => x.Id == post.Id);
        Assert.Equal(2, saved.Categories.Count); // 保留 + 批次二（刪除失敗者仍在）

        // 空批與超量都被明確拒絕。
        await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.BulkDeleteAsync(TaxonomyKind.Category, []));
        var over = Enumerable.Range(0, Limits.BulkMaxItems + 1)
            .Select(_ => (Guid.NewGuid(), Guid.NewGuid())).ToList();
        await Assert.ThrowsAsync<CmsException>(() => admin.Taxonomy.BulkDeleteAsync(TaxonomyKind.Category, over));
    }
}
