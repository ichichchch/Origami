namespace Origami.Tests;

/// <summary>列表快速編輯：標題、代稱與狀態的就地更新，規則與完整儲存一致。</summary>
[Collection(PostgresCollection.Name)]
public sealed class QuickEditTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Token = "isolated-test-service-token-0123456789abcdef";

    private static async Task<ContentSummary> CreatePostAsync(TestActor admin, string title)
    {
        var post = NewContent(title);
        await admin.Content.SaveAsync(post);
        return (await admin.Content.ListAdminAsync(ContentKind.Post)).Items.Single(x => x.Title == title);
    }

    [PostgresFact]
    public async Task Quick_edit_updates_title_slug_and_retires_old_slug()
    {
        using var admin = await App.AdministratorAsync();
        var published = NewContent("快速編輯原文", status: ContentStatus.Published);
        await admin.Content.SaveAsync(published);
        var item = (await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Published)).Items.Single();

        var result = await admin.Content.QuickEditAsync(item.Id, "快速編輯新名", "quick-new-slug", ContentStatus.Published, item.Version);

        Assert.Equal("快速編輯新名", result.Title);
        Assert.Equal("quick-new-slug", result.Slug);
        Assert.Equal(ContentStatus.Published, result.Status);
        Assert.NotEqual(item.Version, result.Version);

        await using var db = await App.CreateContextAsync();
        var redirect = await db.SlugRedirects.AsNoTracking().SingleOrDefaultAsync(x => x.OldSlug == item.Slug);
        Assert.NotNull(redirect);
        Assert.Equal(item.Id, redirect.ContentId);
        // 標題被覆寫前會留下修訂版本。
        Assert.True(await db.ContentRevisions.AnyAsync(x => x.ContentId == item.Id && x.Title == "快速編輯原文"));
    }

    [PostgresFact]
    public async Task Quick_edit_publishing_clears_schedule_and_syncs_embedding_lifecycle()
    {
        using var admin = await App.AdministratorAsync();
        var draft = NewContent("排程草稿");
        draft.Status = ContentStatus.Draft;
        draft.PublishAt = DateTimeOffset.UtcNow.AddHours(2);
        await admin.Content.SaveAsync(draft);
        var item = (await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Draft)).Items.Single();

        var published = await admin.Content.QuickEditAsync(item.Id, item.Title, item.Slug, ContentStatus.Published, item.Version);

        Assert.Equal(ContentStatus.Published, published.Status);
        await using var db = await App.CreateContextAsync();
        var entity = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == item.Id);
        Assert.Null(entity.PublishAt);
        Assert.NotNull(entity.PublishedAt);
        Assert.True(await db.EmbeddingJobs.AnyAsync(x => x.ContentId == item.Id));

        // 撤回為草稿：嵌入工作與向量隨之失效。
        await admin.Content.QuickEditAsync(item.Id, entity.Title, entity.Slug, ContentStatus.Draft, published.Version);
        await using var after = await App.CreateContextAsync();
        Assert.False(await after.EmbeddingJobs.AnyAsync(x => x.ContentId == item.Id));
        var withdrawn = await after.Contents.AsNoTracking().SingleAsync(x => x.Id == item.Id);
        Assert.Equal(ContentStatus.Draft, withdrawn.Status);
    }

    [PostgresFact]
    public async Task Quick_edit_rejects_conflicts_trash_and_invalid_input()
    {
        using var admin = await App.AdministratorAsync();
        var post = await CreatePostAsync(admin, "衝突與驗證");
        var again = await admin.Content.GetEditAsync(post.Id);
        await admin.Content.UpdateAsync(again);

        var conflict = await Assert.ThrowsAsync<CmsException>(() =>
            admin.Content.QuickEditAsync(post.Id, "新標題", post.Slug, ContentStatus.Draft, post.Version));
        Assert.Contains("另一個分頁", conflict.Message, StringComparison.Ordinal);

        var blank = await Assert.ThrowsAsync<CmsException>(() =>
            admin.Content.QuickEditAsync(post.Id, "   ", post.Slug, ContentStatus.Draft, again.Version));
        Assert.Contains("標題", blank.Message, StringComparison.Ordinal);

        var badStatus = await Assert.ThrowsAsync<CmsException>(() =>
            admin.Content.QuickEditAsync(post.Id, "新標題", post.Slug, ContentStatus.Trashed, again.Version));
        Assert.Contains("回收桶", badStatus.Message, StringComparison.Ordinal);

        // 回收桶內容不可快速編輯，須先還原。
        await admin.Content.TrashAsync(post.Id, again.Version, confirmed: true);
        var trashed = (await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Trashed)).Items.Single();
        var trashedRefusal = await Assert.ThrowsAsync<CmsException>(() =>
            admin.Content.QuickEditAsync(post.Id, "新標題", trashed.Slug, ContentStatus.Draft, trashed.Version));
        Assert.Contains("回收桶", trashedRefusal.Message, StringComparison.Ordinal);

        using var anonymous = App.Anonymous();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            anonymous.Content.QuickEditAsync(post.Id, "新標題", "any-slug", ContentStatus.Draft, trashed.Version));
    }

    [PostgresFact]
    public async Task Quick_edit_endpoint_validates_and_executes_over_bff()
    {
        using var actor = await App.AdministratorAsync();
        var item = await CreatePostAsync(actor, "API 快速編輯");
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var client = api.CreateBffClient(Token, await App.AdminUserIdAsync(), await App.AdminSecurityStampAsync());

        using var invalid = await client.PutAsJsonAsync($"/api/v1/admin/contents/{item.Id}/quick",
            new { title = "新標題", slug = "api-quick-slug", status = "Trashed", version = item.Version });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var ok = await client.PutAsJsonAsync($"/api/v1/admin/contents/{item.Id}/quick",
            new { title = "新標題", slug = "api-quick-slug", status = "Published", version = item.Version });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = JsonNode.Parse(await ok.Content.ReadAsStringAsync())!;
        Assert.Equal("api-quick-slug", (string)body["slug"]!);
        Assert.Equal("Published", (string)body["status"]!);

        using var listed = await client.GetAsync("/api/v1/admin/contents?kind=Post&status=Published");
        var list = JsonNode.Parse(await listed.Content.ReadAsStringAsync())!;
        Assert.Equal("新標題", (string)list["items"]![0]!["title"]!);
    }
}
