namespace Origami.Tests;

/// <summary>
/// 受控擴充點（僅原始碼層）：核心事件在關鍵節點通知訂閱者，訂閱者例外不影響主流程。
/// 事件名稱與出站 Webhook 對齊；沒有訂閱者時零成本。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CmsEventTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private sealed class CapturingSubscriber : ICmsEventSubscriber
    {
        public List<CmsEvent> Received { get; } = [];
        public Task HandleAsync(CmsEvent @event, CancellationToken ct = default)
        {
            Received.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSubscriber : ICmsEventSubscriber
    {
        public Task HandleAsync(CmsEvent @event, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Bus_isolates_subscriber_failures_and_flattens_payloads()
    {
        var captured = new CapturingSubscriber();
        var bus = new CmsEventBus(new ICmsEventSubscriber[] { new ThrowingSubscriber(), captured });
        await bus.PublishAsync("sample.event", SiteDefaults.DefaultSiteId,
            new { title = "標題", publishedAt = (DateTimeOffset?)null }, CancellationToken.None);
        var @event = Assert.Single(captured.Received);
        Assert.Equal("sample.event", @event.Name);
        Assert.Equal(SiteDefaults.DefaultSiteId, @event.SiteId);
        Assert.Equal("標題", @event.Data["title"]);
        Assert.Null(@event.Data["publishedAt"]);
    }

    [PostgresFact]
    public async Task Content_lifecycle_comment_and_settings_events_are_published()
    {
        using var admin = await App.AdministratorAsync();
        var captured = new CapturingSubscriber();
        var bus = new CmsEventBus(new ICmsEventSubscriber[] { captured });
        var content = new ContentService(App.ContextFactory,
            admin.Services.GetRequiredService<AdminAccess>(), admin.Services.GetRequiredService<TimeProvider>(), events: bus);
        var comments = new CommentService(App.ContextFactory,
            admin.Services.GetRequiredService<AdminAccess>(), admin.Services.GetRequiredService<TimeProvider>(), events: bus);
        var settings = new SettingsService(App.ContextFactory,
            admin.Services.GetRequiredService<AdminAccess>(), events: bus);

        // 發布 → 撤回 → 回收桶 → 永久刪除：四個生命週期事件依序通知。
        var post = await content.SaveAsync(NewContent("事件文章", status: ContentStatus.Published));
        var edit = await content.GetEditAsync(post.Id!.Value);
        edit.Status = ContentStatus.Draft;
        await content.UpdateAsync(edit);
        await content.TrashAsync(post.Id.Value, edit.Version, confirmed: true);
        await using (var db = await App.CreateContextAsync())
        {
            var trashed = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == post.Id);
            await content.DeleteAsync(post.Id.Value, trashed.Version, confirmed: true);
        }

        var settingsModel = await admin.Settings.GetAsync();
        settingsModel.CommentsEnabled = true;
        await settings.SaveAsync(settingsModel);

        var commentPost = await content.SaveAsync(NewContent("評論事件文章", status: ContentStatus.Published));
        await comments.PostAsync(commentPost.Id!.Value,
            new CommentService.CommentPostInput("訪客甲", null, "事件留言", false));
        var queue = await comments.ListAdminAsync(CommentStatus.Pending, ct: default);
        var pending = Assert.Single(queue.Items, x => x.AuthorName == "訪客甲");
        await comments.ModerateAsync(pending.Id, CommentStatus.Approved, pending.Version);

        Assert.Equal([
            "content.published", "content.withdrawn", "content.trashed", "content.deleted",
            "settings.updated", "content.published", "comment.created", "comment.approved"
        ], captured.Received.Select(x => x.Name).ToList());
        Assert.All(captured.Received.Where(x => x.Name != "user.created"), x => Assert.Equal(SiteDefaults.DefaultSiteId, x.SiteId));
        Assert.Equal("事件文章", captured.Received[0].Data["title"]);
    }

    [PostgresFact]
    public async Task User_creation_event_is_published_with_global_scope()
    {
        using var admin = await App.AdministratorAsync();
        var captured = new CapturingSubscriber();
        var bus = new CmsEventBus(new ICmsEventSubscriber[] { captured });
        var administration = new SiteAdministrationService(App.ContextFactory,
            admin.Services.GetRequiredService<AdminAccess>(),
            admin.Services.GetRequiredService<UserManager<AdminUser>>(),
            admin.Services.GetRequiredService<TimeProvider>(), events: bus);

        var created = await administration.CreateUserAsync(new UserCreateInput
        {
            UserName = "event-user", Password = "EventUser#2026", Email = ""
        });

        var @event = Assert.Single(captured.Received);
        Assert.Equal("user.created", @event.Name);
        Assert.Equal(Guid.Empty, @event.SiteId); // 帳號屬識別網域：非站點範圍事件
        Assert.Equal(created.Id, @event.Data["id"]);
    }
}
