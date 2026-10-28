namespace Origami.Tests;

/// <summary>內容清單批次操作（解鎖於 2026-10-27）：逐筆執行、逐筆回報。</summary>
[Collection(PostgresCollection.Name)]
public sealed class BulkOperationsTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Token = "isolated-test-service-token-0123456789abcdef";

    private static async Task<ContentSummary> CreatePostAsync(TestActor admin, string title)
    {
        var post = NewContent(title);
        await admin.Content.SaveAsync(post);
        return (await admin.Content.ListAdminAsync(ContentKind.Post)).Items.Single(x => x.Title == title);
    }

    [PostgresFact]
    public async Task Bulk_reports_each_item_and_survives_single_failures()
    {
        using var admin = await App.AdministratorAsync();
        var good = await CreatePostAsync(admin, "批次正常文章");
        var conflict = await CreatePostAsync(admin, "批次衝突文章");
        var missing = Guid.NewGuid();

        // 再儲存一次讓列表中的版本過期，模擬另一個分頁已更新的衝突。
        var editedAgain = await admin.Content.GetEditAsync(conflict.Id);
        await admin.Content.UpdateAsync(editedAgain);

        var results = await admin.Content.BulkAsync(BulkAction.Trash,
            [(good.Id, good.Version), (conflict.Id, conflict.Version), (missing, Guid.NewGuid())]);
        Assert.Equal(3, results.Count);
        Assert.True(results.Single(x => x.Id == good.Id).Succeeded);
        Assert.False(results.Single(x => x.Id == conflict.Id).Succeeded);
        Assert.Contains("另一個分頁", results.Single(x => x.Id == conflict.Id).Message, StringComparison.Ordinal);
        Assert.False(results.Single(x => x.Id == missing).Succeeded);
        Assert.Contains("不存在", results.Single(x => x.Id == missing).Message, StringComparison.Ordinal);

        // 只有成功的那筆真的進了回收桶。
        var trashed = await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Trashed);
        Assert.Equal(good.Id, trashed.Items.Single().Id);

        // 批次還原：以回收桶列表的新版本執行。
        var trashedItem = trashed.Items.Single();
        var restored = await admin.Content.BulkAsync(BulkAction.Restore, [(trashedItem.Id, trashedItem.Version)]);
        Assert.True(restored.Single().Succeeded);
        var drafts = await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Draft);
        Assert.Contains(drafts.Items, x => x.Id == good.Id);

        // 批次永久刪除：再以草稿列表的最新版本執行。
        var draftItem = drafts.Items.Single(x => x.Id == good.Id);
        var deleted = await admin.Content.BulkAsync(BulkAction.Delete, [(draftItem.Id, draftItem.Version)]);
        Assert.True(deleted.Single().Succeeded);
        var remaining = await admin.Content.ListAdminAsync(ContentKind.Post);
        Assert.DoesNotContain(remaining.Items, x => x.Id == good.Id);
    }

    [PostgresFact]
    public async Task Bulk_rejects_empty_and_oversized_batches_and_requires_admin()
    {
        using var admin = await App.AdministratorAsync();
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.BulkAsync(BulkAction.Trash, []));
        var tooMany = Enumerable.Range(0, Limits.BulkMaxItems + 1)
            .Select(_ => (Guid.NewGuid(), Guid.NewGuid())).ToList();
        var refused = await Assert.ThrowsAsync<CmsException>(() => admin.Content.BulkAsync(BulkAction.Trash, tooMany));
        Assert.Contains($"{Limits.BulkMaxItems}", refused.Message, StringComparison.Ordinal);

        using var anonymous = App.Anonymous();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            anonymous.Content.BulkAsync(BulkAction.Trash, [(Guid.NewGuid(), Guid.NewGuid())]));
    }

    [PostgresFact]
    public async Task Bulk_endpoint_validates_input_and_executes_over_bff()
    {
        using var actor = await App.AdministratorAsync();
        var post = await CreatePostAsync(actor, "API 批次文章");
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var adminClient = api.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        // BFF 合約：管理端點要求服務通行證＋有效的管理員工作階段戳記。
        adminClient.DefaultRequestHeaders.Add(AdminSession.StampHeader, await App.AdminSecurityStampAsync());
        adminClient.DefaultRequestHeaders.Add(AccessClaims.UserHeader, await App.AdminUserIdAsync());

        using var invalidAction = await adminClient.PostAsJsonAsync("/api/v1/admin/contents/bulk",
            new { action = "explode", items = new[] { new { id = post.Id, version = post.Version } } });
        Assert.Equal(HttpStatusCode.BadRequest, invalidAction.StatusCode);

        using var emptyItems = await adminClient.PostAsJsonAsync("/api/v1/admin/contents/bulk",
            new { action = "trash", items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, emptyItems.StatusCode);

        // 缺少 items 欄位（null）與超過上限的批次同樣在端點層被拒。
        using var missingItems = await adminClient.PostAsJsonAsync("/api/v1/admin/contents/bulk",
            new { action = "trash" });
        Assert.Equal(HttpStatusCode.BadRequest, missingItems.StatusCode);
        var oversized = Enumerable.Range(0, Limits.BulkMaxItems + 1)
            .Select(_ => new { id = Guid.NewGuid(), version = Guid.NewGuid() }).ToArray();
        using var tooMany = await adminClient.PostAsJsonAsync("/api/v1/admin/contents/bulk",
            new { action = "trash", items = oversized });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        using var ok = await adminClient.PostAsJsonAsync("/api/v1/admin/contents/bulk",
            new { action = "trash", items = new[] { new { id = post.Id, version = post.Version } } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = JsonNode.Parse(await ok.Content.ReadAsStringAsync())!;
        Assert.True((bool)body[0]!["succeeded"]!);

        using var listed = await adminClient.GetAsync("/api/v1/admin/contents?kind=Post&status=Trashed");
        var trashed = JsonNode.Parse(await listed.Content.ReadAsStringAsync())!;
        Assert.Equal("API 批次文章", (string)trashed["items"]![0]!["title"]!);
    }
}
