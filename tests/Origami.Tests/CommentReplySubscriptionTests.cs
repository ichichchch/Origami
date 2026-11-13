using Origami.Core.Models;
using Origami.Core.Services;

namespace Origami.Tests;

/// <summary>
/// 評論回覆訂閱：訂閱記錄、回覆通知（自動核可／管理員回覆／審核通過三個觸發點）、
/// 一次性退訂與冪等性；垃圾評論不留訂閱資料。管理員新評論通知屬既有功能（EmailNotificationsEnabled）。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CommentReplySubscriptionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private TestEmailSender Mail => App.Services.GetRequiredService<TestEmailSender>();

    private async Task<(Guid ContentId, CommentService Comments)> SetupOpenCommentsAsync(TestActor admin, bool autoApprove)
    {
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        settings.CommentAutoApprove = autoApprove;
        settings.EmailNotificationsEnabled = true;
        await admin.Settings.SaveAsync(settings);
        await using (var db = await App.CreateContextAsync())
        {
            var site = await db.Sites.SingleAsync(x => x.Id == db.CurrentSiteId);
            site.HostName = "notify.example.test";
            await db.SaveChangesAsync();
        }
        var post = await admin.Content.SaveAsync(NewContent("訂閱測試文章", status: ContentStatus.Published));
        return (post.Id!.Value, admin.Services.GetRequiredService<CommentService>());
    }

    [PostgresFact]
    public async Task Reply_to_subscribed_comment_sends_notification_with_unsubscribe_link()
    {
        using var admin = await App.AdministratorAsync();
        var (contentId, comments) = await SetupOpenCommentsAsync(admin, autoApprove: true);
        Mail.Reset();

        await comments.PostAsync(contentId, new CommentService.CommentPostInput(
            "訂閱者", null, "這是我的提問", false, Email: "subscriber@example.test", NotifyReplies: true));
        Assert.Empty(Mail.Messages); // 自己的評論不觸發訂閱通知（管理員通知視設定另計，此處 EmailNotifications 開啟會寄給管理員）
        var adminNotices = Mail.Messages.Count;

        await comments.PostAsync(contentId, new CommentService.CommentPostInput(
            "路人甲", null, "我來回覆", false, ParentId: await FirstCommentIdAsync(contentId)));
        // 回覆核可（自動核可）→ 訂閱者收到通知，內文含一次性退訂連結。
        var notice = Mail.Messages.Skip(adminNotices).SingleOrDefault(x => x.To == "subscriber@example.test");
        Assert.NotNull(notice);
        Assert.Contains("路人甲", notice!.Body);
        Assert.Contains("/comments/unsubscribe?token=", notice.Body);
    }

    [PostgresFact]
    public async Task Pending_reply_notifies_only_after_moderation_and_admin_reply_notifies_immediately()
    {
        using var admin = await App.AdministratorAsync();
        var (contentId, comments) = await SetupOpenCommentsAsync(admin, autoApprove: false);
        await comments.PostAsync(contentId, new CommentService.CommentPostInput(
            "訂閱者", null, "提問", false, Email: "subscriber@example.test", NotifyReplies: true));
        var subscriberComment = Assert.Single((await comments.ListAdminAsync(CommentStatus.Pending)).Items);
        await comments.ModerateAsync(subscriberComment.Id, CommentStatus.Approved, subscriberComment.Version);
        var parentId = subscriberComment.Id;
        Mail.Reset();

        // 審核制：待審回覆不寄通知。
        await comments.PostAsync(contentId, new CommentService.CommentPostInput(
            "路人", null, "待審的回覆", false, ParentId: parentId));
        Assert.DoesNotContain(Mail.Messages, x => x.To == "subscriber@example.test");

        // 審核通過的那一刻寄出。
        var pending = Assert.Single((await comments.ListAdminAsync(CommentStatus.Pending)).Items);
        await comments.ModerateAsync(pending.Id, CommentStatus.Approved, pending.Version);
        Assert.Contains(Mail.Messages, x => x.To == "subscriber@example.test");

        // 管理員直接回覆：立即核可立即通知。
        Mail.Reset();
        await comments.ReplyAdminAsync(parentId, "官方回覆內容");
        Assert.Contains(Mail.Messages, x => x.To == "subscriber@example.test");
    }

    [PostgresFact]
    public async Task Unsubscribe_is_idempotent_and_spam_keeps_no_subscription()
    {
        using var admin = await App.AdministratorAsync();
        var (contentId, comments) = await SetupOpenCommentsAsync(admin, autoApprove: true);
        // 蜜罐命中（靜默垃圾）：不留訂閱資料。
        await comments.PostAsync(contentId, new CommentService.CommentPostInput(
            "機器人", null, "spam http://a.test http://b.test http://c.test http://d.test", true,
            Email: "bot@example.test", NotifyReplies: true));

        await comments.PostAsync(contentId, new CommentService.CommentPostInput(
            "訂閱者", null, "正常評論", false, Email: "subscriber@example.test", NotifyReplies: true));
        var token = await SubscriptionTokenAsync(contentId, "subscriber@example.test");
        Assert.NotNull(token);

        // 退訂冪等成功；重複退訂與亂數 token 都不報錯、不揭露訂閱是否存在。
        await comments.UnsubscribeRepliesAsync(token!.Value);
        await comments.UnsubscribeRepliesAsync(token.Value);
        await comments.UnsubscribeRepliesAsync(Guid.NewGuid());
        Assert.Null(await SubscriptionTokenAsync(contentId, "subscriber@example.test"));

        // 退訂後的回覆不再通知。
        Mail.Reset();
        await comments.PostAsync(contentId, new CommentService.CommentPostInput(
            "路人", null, "退訂後的回覆", false, ParentId: await FirstCommentIdAsync(contentId)));
        Assert.DoesNotContain(Mail.Messages, x => x.To == "subscriber@example.test");
    }

    [PostgresFact]
    public async Task Subscription_requires_email_when_notify_is_checked()
    {
        using var admin = await App.AdministratorAsync();
        var (contentId, comments) = await SetupOpenCommentsAsync(admin, autoApprove: true);
        await Assert.ThrowsAsync<CmsException>(() => comments.PostAsync(contentId, new CommentService.CommentPostInput(
            "訪客", null, "沒留信箱", false, Email: "", NotifyReplies: true)));
        // 只留信箱未勾選：不視為訂閱。
        await comments.PostAsync(contentId, new CommentService.CommentPostInput(
            "訪客二", null, "有信箱但沒勾", false, Email: "someone@example.test", NotifyReplies: false));
        Assert.Null(await SubscriptionTokenAsync(contentId, "someone@example.test"));
    }

    private async Task<Guid> FirstCommentIdAsync(Guid contentId)
    {
        await using var db = await App.CreateContextAsync();
        return await db.Comments.AsNoTracking()
            .Where(x => x.ContentId == contentId && x.Status == CommentStatus.Approved)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => x.Id).FirstAsync();
    }

    private async Task<Guid?> SubscriptionTokenAsync(Guid contentId, string email)
    {
        await using var db = await App.CreateContextAsync();
        return await db.Comments.AsNoTracking()
            .Where(x => x.ContentId == contentId && x.SubscriberEmail == email)
            .Select(x => x.UnsubscribeToken).FirstOrDefaultAsync();
    }
}
