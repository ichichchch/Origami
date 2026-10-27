namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ContentLifecycleTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private ContentLifecycleWorker Worker => new(App.Services, TimeProvider.System, NullLogger<ContentLifecycleWorker>.Instance);

    [PostgresFact]
    public async Task Trashed_content_disappears_everywhere_and_can_be_restored()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("回收桶文章", status: ContentStatus.Published);
        post.Slug = "trash-post";
        var saved = await admin.Content.SaveAsync(post);
        await App.Worker.ProcessPendingAsync();
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/posts/trash-post")).StatusCode);

        await admin.Content.TrashAsync(saved.Id!.Value, saved.Version, true);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/trash-post")).StatusCode);
        Assert.DoesNotContain("/posts/trash-post", await (await client.GetAsync("/sitemap.xml")).Content.ReadAsStringAsync());
        await using (var db = await App.CreateContextAsync())
        {
            Assert.False(await db.ContentEmbeddings.AnyAsync(x => x.ContentId == saved.Id));
            Assert.False(await db.EmbeddingJobs.AnyAsync(x => x.ContentId == saved.Id));
        }
        var counts = await admin.Content.CountAdminAsync(ContentKind.Post);
        Assert.Equal((0, 1), (counts.Drafts, counts.Trashed));
        Assert.Equal(0, counts.Published);
        // 預設管理列表隱藏回收桶內容；用回收桶篩選才看得到。
        Assert.Empty((await admin.Content.ListAdminAsync(ContentKind.Post)).Items);
        var trashedList = await admin.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Trashed);
        Assert.Equal("trash-post", trashedList.Items.Single().Slug);

        // 回收桶內容在還原前拒絕直接編輯。
        var edit = await admin.Content.GetEditAsync(saved.Id.Value);
        var refused = await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(edit));
        Assert.Contains("還原", refused.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.TrashAsync(saved.Id.Value, edit.Version, true));

        await admin.Content.RestoreAsync(saved.Id.Value, edit.Version);
        var restored = await admin.Content.GetEditAsync(saved.Id.Value);
        Assert.Equal(ContentStatus.Draft, restored.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/trash-post")).StatusCode);
        Assert.Equal((1, 0), ((await admin.Content.CountAdminAsync(ContentKind.Post)).Drafts, (await admin.Content.CountAdminAsync(ContentKind.Post)).Trashed));
    }

    [PostgresFact]
    public async Task The_purge_removes_only_trash_past_the_retention_window()
    {
        using var admin = await App.AdministratorAsync();
        var old = NewContent("過期垃圾", status: ContentStatus.Published);
        var oldSaved = await admin.Content.SaveAsync(old);
        var fresh = NewContent("新鮮垃圾", status: ContentStatus.Published);
        var freshSaved = await admin.Content.SaveAsync(fresh);
        await admin.Content.TrashAsync(oldSaved.Id!.Value, oldSaved.Version, true);
        await admin.Content.TrashAsync(freshSaved.Id!.Value, freshSaved.Version, true);
        await using (var db = await App.CreateContextAsync())
        {
            var expired = await db.Contents.SingleAsync(x => x.Id == oldSaved.Id);
            expired.DeletedAt = DateTimeOffset.UtcNow.AddDays(-Limits.TrashRetentionDays - 1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await Worker.PurgeAsync());
        await using var after = await App.CreateContextAsync();
        Assert.False(await after.Contents.AnyAsync(x => x.Id == oldSaved.Id));
        Assert.True(await after.Contents.AnyAsync(x => x.Id == freshSaved.Id));
        // 修訂版本隨內容一併銷毀。
        Assert.False(await after.ContentRevisions.AnyAsync(x => x.ContentId == oldSaved.Id));
        Assert.Equal(0, await Worker.PurgeAsync());
    }

    [PostgresFact]
    public async Task Every_save_keeps_a_capped_revision_of_the_previous_state()
    {
        using var admin = await App.AdministratorAsync();
        var post = await admin.Content.SaveAsync(NewContent("修訂來源"));
        Guid currentId = post.Id!.Value;
        for (var i = 1; i <= Limits.RevisionKeep + 4; i++)
        {
            var edit = await admin.Content.GetEditAsync(currentId);
            edit.Markdown = "第 " + i + " 版內容";
            await admin.Content.UpdateAsync(edit);
        }
        var revisions = await admin.Content.ListRevisionsAsync(currentId);
        Assert.Equal(Limits.RevisionKeep, revisions.Count);
        // 最新一筆修訂版本保存最後一次儲存前的狀態。
        var newest = await admin.Content.GetRevisionAsync(revisions[0].Id);
        Assert.NotNull(newest);
        Assert.Equal("第 " + (Limits.RevisionKeep + 3) + " 版內容", newest!.Markdown);
        var current = await admin.Content.GetEditAsync(currentId);
        Assert.Equal("第 " + (Limits.RevisionKeep + 4) + " 版內容", current.Markdown);
        // 修訂版本隨內容一併消失。
        await admin.Content.DeleteAsync(currentId, current.Version, true);
        Assert.Empty(await admin.Content.ListRevisionsAsync(currentId));
    }

    [PostgresFact]
    public async Task Scheduled_drafts_publish_when_due_and_only_when_due()
    {
        using var admin = await App.AdministratorAsync();
        // 排程時間必須設定在夠遠的未來。
        var tooSoon = NewContent("太近的排程");
        tooSoon.PublishAt = DateTimeOffset.UtcNow.AddMinutes(Limits.ScheduleLeadMinutes - 1);
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(tooSoon));
        var publishedWithSchedule = NewContent("帶排程的發布", status: ContentStatus.Published);
        publishedWithSchedule.PublishAt = DateTimeOffset.UtcNow.AddDays(1);
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(publishedWithSchedule));

        var scheduled = NewContent("排程文章");
        scheduled.Slug = "scheduled-post";
        scheduled.PublishAt = DateTimeOffset.UtcNow.AddMinutes(Limits.ScheduleLeadMinutes + 1);
        var saved = await admin.Content.SaveAsync(scheduled);
        Assert.Equal(saved.PublishAt, scheduled.PublishAt);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/scheduled-post")).StatusCode);
        Assert.Equal(0, await Worker.PromoteAsync());

        // 把已儲存的排程硬改到過去；背景工作會在排程時刻發布。
        await using (var db = await App.CreateContextAsync())
        {
            var entity = await db.Contents.SingleAsync(x => x.Id == saved.Id);
            entity.PublishAt = DateTimeOffset.UtcNow.AddSeconds(-5);
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await Worker.PromoteAsync());
        var promoted = await admin.Content.GetEditAsync(saved.Id!.Value);
        Assert.Equal(ContentStatus.Published, promoted.Status);
        Assert.Null(promoted.PublishAt);
        Assert.NotNull(promoted.PublishedAt);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/posts/scheduled-post")).StatusCode);
        // 排程轉為發布時，與手動發布一樣會排入嵌入工作。
        await using var check = await App.CreateContextAsync();
        Assert.True(await check.EmbeddingJobs.AnyAsync(x => x.ContentId == saved.Id));

        // 撤回會一併清除排程。
        var withdrawn = await admin.Content.GetEditAsync(saved.Id.Value);
        withdrawn.Status = ContentStatus.Draft;
        await admin.Content.UpdateAsync(withdrawn);
        Assert.Null((await admin.Content.GetEditAsync(saved.Id.Value)).PublishAt);
    }

    [PostgresFact]
    public async Task Preview_tokens_share_drafts_and_die_on_publish_trash_or_revoke()
    {
        using var admin = await App.AdministratorAsync();
        var draft = NewContent("預覽草稿");
        draft.Slug = "preview-draft";
        var saved = await admin.Content.SaveAsync(draft);
        var token = await admin.Content.CyclePreviewTokenAsync(saved.Id!.Value);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var preview = await client.GetAsync("/preview/" + token);
        Assert.True(preview.IsSuccessStatusCode);
        var html = await preview.Content.ReadAsStringAsync();
        Assert.Contains("草稿預覽", WebUtility.HtmlDecode(html));
        Assert.Contains("noindex", html);
        Assert.Equal("noindex, nofollow", preview.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Contains("Disallow: /preview", await (await client.GetAsync("/robots.txt")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/preview/" + Guid.NewGuid())).StatusCode);

        // 發布後 token 退役，改為永久轉址到公開網址。
        var publish = await admin.Content.GetEditAsync(saved.Id.Value);
        publish.Status = ContentStatus.Published;
        await admin.Content.UpdateAsync(publish);
        using var moved = await client.GetAsync("/preview/" + token);
        Assert.Equal(HttpStatusCode.MovedPermanently, moved.StatusCode);
        Assert.Equal("/posts/preview-draft", moved.Headers.Location!.ToString());

        // 換發會替換 token：舊 token 立即失效，新 token 仍可使用。
        var second = await admin.Content.CyclePreviewTokenAsync(saved.Id.Value);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/preview/" + token)).StatusCode);
        using var movedAgain = await client.GetAsync("/preview/" + second);
        Assert.Equal(HttpStatusCode.MovedPermanently, movedAgain.StatusCode);
        Assert.Equal("/posts/preview-draft", movedAgain.Headers.Location!.ToString());
        await admin.Content.RevokePreviewTokenAsync(saved.Id.Value);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/posts/preview-draft")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/preview/" + second)).StatusCode);
    }
}
