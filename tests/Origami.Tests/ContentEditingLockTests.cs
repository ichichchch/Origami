namespace Origami.Tests;

/// <summary>
/// 編輯占用鎖：心跳取得／刷新占用、他人占用不可靜默搶走、明確接管可取代、
/// 逾期占用自動失效、離開只釋放自己的占用。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentEditingLockTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private async Task<(Guid ContentId, string AuthorId)> CreateOwnedContentAsync(string displayName)
    {
        var user = await CreateMemberAsync(SiteRole.Editor);
        using var admin = await App.AdministratorAsync();
        var member = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == user.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = member.UserName, Role = member.Role, DisplayName = displayName, Version = member.Version
        });
        using var actor = await App.UserAsync(user.Id);
        var saved = await actor.Content.SaveAsync(NewContent("占用文章"));
        return (saved.Id!.Value, user.Id);
    }

    [PostgresFact]
    public async Task Heartbeat_acquires_and_refreshes_the_lock_for_its_owner()
    {
        var (contentId, authorId) = await CreateOwnedContentAsync("占用者");
        using var actor = await App.AdministratorAsync();
        var actorId = (await actor.Access.GetCurrentAccessAsync()).UserId;
        var state = await actor.Content.TouchEditingAsync(contentId);
        Assert.Equal(actorId, state.UserId);
        Assert.False(state.HeldByOther);

        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == contentId);
        Assert.Equal(actorId, row.EditingById);
        Assert.NotNull(row.EditingAt);
        Assert.NotEqual(authorId, row.EditingById); // 第二位編輯者取得了無人占用的內容
    }

    [PostgresFact]
    public async Task Another_editor_cannot_silently_steal_a_fresh_lock()
    {
        var (contentId, _) = await CreateOwnedContentAsync("原占用者");
        using var holder = await App.AdministratorAsync();
        var acquired = await holder.Content.TouchEditingAsync(contentId);
        Assert.False(acquired.HeldByOther);

        var other = await CreateMemberAsync(SiteRole.Editor, name: "other-editor");
        using var otherActor = await App.UserAsync(other.Id);
        var state = await otherActor.Content.TouchEditingAsync(contentId);
        Assert.True(state.HeldByOther);
        Assert.Equal(acquired.UserId, state.UserId);

        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == contentId);
        Assert.Equal(acquired.UserId, row.EditingById);
    }

    [PostgresFact]
    public async Task Explicit_takeover_replaces_a_fresh_lock_and_edit_view_reports_it()
    {
        var (contentId, _) = await CreateOwnedContentAsync("原占用者");
        using var holder = await App.AdministratorAsync();
        var acquired = await holder.Content.TouchEditingAsync(contentId);

        var other = await CreateMemberAsync(SiteRole.Editor, name: "other-editor");
        using var admin = await App.AdministratorAsync();
        var member = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == other.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = member.UserName, Role = member.Role, DisplayName = "接管者", Version = member.Version
        });
        using var otherActor = await App.UserAsync(other.Id);
        var state = await otherActor.Content.TouchEditingAsync(contentId, takeOver: true);
        Assert.False(state.HeldByOther);

        // 原占用者的下一次心跳會看到自己已不再持有；編輯檢視也回報他人占用。
        var heartbeat = await holder.Content.TouchEditingAsync(contentId);
        Assert.True(heartbeat.HeldByOther);
        var edit = await holder.Content.GetEditAsync(contentId);
        Assert.True(edit.EditingByOther);
        Assert.Equal("接管者", edit.EditingByName);
    }

    [PostgresFact]
    public async Task Stale_locks_expire_and_can_be_acquired_again()
    {
        var (contentId, _) = await CreateOwnedContentAsync("原占用者");
        using var holder = await App.AdministratorAsync();
        await holder.Content.TouchEditingAsync(contentId);

        // 直接回寫占用時間，模擬心跳逾時（不動並發版本，只更新心跳欄位）。
        await using (var db = await App.CreateContextAsync())
        {
            var row = await db.Contents.SingleAsync(x => x.Id == contentId);
            row.EditingAt = DateTimeOffset.UtcNow.AddSeconds(-Limits.EditingLockSeconds - 10);
            await db.SaveChangesAsync();
        }

        var other = await CreateMemberAsync(SiteRole.Editor, name: "late-editor");
        using var otherActor = await App.UserAsync(other.Id);
        var state = await otherActor.Content.TouchEditingAsync(contentId);
        Assert.False(state.HeldByOther);
        await using var db2 = await App.CreateContextAsync();
        var updated = await db2.Contents.AsNoTracking().SingleAsync(x => x.Id == contentId);
        Assert.NotEqual("原占用者", updated.EditingByName);
    }

    [PostgresFact]
    public async Task Release_only_clears_the_callers_own_lock()
    {
        var (contentId, _) = await CreateOwnedContentAsync("原占用者");
        using var holder = await App.AdministratorAsync();
        var acquired = await holder.Content.TouchEditingAsync(contentId);

        var other = await CreateMemberAsync(SiteRole.Editor, name: "bystander");
        using var otherActor = await App.UserAsync(other.Id);
        await otherActor.Content.ReleaseEditingAsync(contentId);
        await using (var db = await App.CreateContextAsync())
        {
            var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == contentId);
            Assert.Equal(acquired.UserId, row.EditingById);
        }

        await holder.Content.ReleaseEditingAsync(contentId);
        await using var db2 = await App.CreateContextAsync();
        var cleared = await db2.Contents.AsNoTracking().SingleAsync(x => x.Id == contentId);
        Assert.Null(cleared.EditingById);
        Assert.Equal("", cleared.EditingByName);
        Assert.Null(cleared.EditingAt);
    }

    [PostgresFact]
    public async Task Authors_can_acquire_refresh_and_release_their_drafts_and_pending_work()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        using var actor = await App.UserAsync(author.Id);
        foreach (var status in new[] { ContentStatus.Draft, ContentStatus.Pending })
        {
            var saved = await actor.Content.SaveAsync(NewContent("Author lock " + status, status: status));
            var id = saved.Id!.Value;
            var acquired = await actor.Content.TouchEditingAsync(id);
            Assert.Equal(author.Id, acquired.UserId);
            Assert.False(acquired.HeldByOther);
            var next = acquired.At!.Value.AddSeconds(1);
            var service = new ContentService(actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
                actor.Access, new FixedClock(next));
            var refreshed = await service.TouchEditingAsync(id);
            Assert.Equal(author.Id, refreshed.UserId);
            Assert.Equal(next, refreshed.At);
            Assert.False(refreshed.HeldByOther);
            await actor.Content.ReleaseEditingAsync(id);
            await using var db = await App.CreateContextAsync();
            var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Null(row.EditingById);
            Assert.Null(row.EditingAt);
            Assert.Equal(saved.Version, row.Version);
            Assert.Equal(status, row.Status);
            Assert.False(await db.ContentRevisions.AnyAsync(x => x.ContentId == id));
            Assert.False(await db.EmbeddingJobs.AnyAsync(x => x.ContentId == id));
        }
    }

    [PostgresFact]
    public async Task Authors_cannot_touch_other_authors_published_scheduled_or_trashed_content()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        var other = await CreateMemberAsync(SiteRole.Author, name: "other-author");
        var editorUser = await CreateMemberAsync(SiteRole.Editor, name: "lock-editor");
        using var actor = await App.UserAsync(author.Id);
        using var otherActor = await App.UserAsync(other.Id);
        using var editor = await App.UserAsync(editorUser.Id);
        var others = await otherActor.Content.SaveAsync(NewContent("Others lock"));
        var othersPending = await otherActor.Content.SaveAsync(NewContent("Others pending lock", status: ContentStatus.Pending));
        var published = await actor.Content.SaveAsync(NewContent("Published lock"));
        published.Status = ContentStatus.Published;
        await editor.Content.UpdateAsync(published);
        var scheduled = await actor.Content.SaveAsync(NewContent("Scheduled lock"));
        scheduled.PublishAt = DateTimeOffset.UtcNow.AddHours(1);
        await editor.Content.UpdateAsync(scheduled);
        var trashed = await actor.Content.SaveAsync(NewContent("Trashed lock"));
        await editor.Content.TrashAsync(trashed.Id!.Value, trashed.Version, true);
        foreach (var saved in new[] { others, othersPending, published, scheduled, trashed })
        {
            var id = saved.Id!.Value;
            await editor.Content.TouchEditingAsync(id);
            await using var db = await App.CreateContextAsync();
            var before = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
            foreach (var takeOver in new[] { false, true })
                await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.TouchEditingAsync(id, takeOver));
            var after = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        }
    }

    [PostgresFact]
    public async Task Authors_release_their_lock_after_publication_but_cannot_release_others_content()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        var editorUser = await CreateMemberAsync(SiteRole.Editor, name: "release-editor");
        using var actor = await App.UserAsync(author.Id);
        using var editor = await App.UserAsync(editorUser.Id);
        var saved = await actor.Content.SaveAsync(NewContent("Release after publication"));
        await actor.Content.TouchEditingAsync(saved.Id!.Value);
        saved.Status = ContentStatus.Published;
        await editor.Content.UpdateAsync(saved);
        await actor.Content.ReleaseEditingAsync(saved.Id.Value);
        await using var db = await App.CreateContextAsync();
        var released = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == saved.Id);
        Assert.Null(released.EditingById);
        Assert.Null(released.EditingAt);
        Assert.Equal(ContentStatus.Published, released.Status);
        Assert.Equal(saved.Version, released.Version);

        var others = await editor.Content.SaveAsync(NewContent("Others content with authors lock"));
        await db.Contents.Where(x => x.Id == others.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.EditingById, author.Id).SetProperty(x => x.EditingAt, DateTimeOffset.UtcNow));
        var before = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == others.Id);
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.ReleaseEditingAsync(others.Id!.Value));
        var after = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == others.Id);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
    }

    [PostgresFact]
    public async Task Competing_editors_cannot_acquire_the_same_unlocked_version()
    {
        var (id, ownerId) = await CreateOwnedContentAsync("First editor");
        using var first = await App.UserAsync(ownerId);
        var other = await CreateMemberAsync(SiteRole.Editor, name: "winning-editor");
        using var winner = await App.UserAsync(other.Id);
        var version = (await first.Content.GetEditAsync(id)).Version;
        var interceptor = new EditingWriteInterceptor(async ct => { await winner.Content.TouchEditingAsync(id, ct: ct); });
        var delayed = InterceptEditingWrite(first, interceptor);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var state = await delayed.TouchEditingAsync(id, ct: timeout.Token);
        Assert.True(interceptor.Entered);
        Assert.True(state.HeldByOther);
        Assert.Equal(other.Id, state.UserId);
        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(row.EditingAt, state.At);
        Assert.Equal(row.EditingByName, state.DisplayName);
        Assert.Equal(other.Id, row.EditingById);
        Assert.Equal(version, row.Version);
    }

    [PostgresFact]
    public async Task A_delayed_refresh_does_not_overwrite_an_explicit_takeover()
    {
        var (id, ownerId) = await CreateOwnedContentAsync("Original editor");
        using var owner = await App.UserAsync(ownerId);
        await owner.Content.TouchEditingAsync(id);
        var other = await CreateMemberAsync(SiteRole.Editor, name: "takeover-editor");
        using var winner = await App.UserAsync(other.Id);
        var version = (await owner.Content.GetEditAsync(id)).Version;
        var interceptor = new EditingWriteInterceptor(async ct => { await winner.Content.TouchEditingAsync(id, takeOver: true, ct: ct); });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var state = await InterceptEditingWrite(owner, interceptor).TouchEditingAsync(id, ct: timeout.Token);
        Assert.True(interceptor.Entered);
        Assert.True(state.HeldByOther);
        Assert.Equal(other.Id, state.UserId);
        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(row.EditingAt, state.At);
        Assert.Equal(other.Id, row.EditingById);
        Assert.Equal(version, row.Version);
    }

    [PostgresFact]
    public async Task A_delayed_takeover_does_not_overwrite_a_newer_heartbeat()
    {
        var (id, ownerId) = await CreateOwnedContentAsync("Original editor");
        using var owner = await App.UserAsync(ownerId);
        var acquired = await owner.Content.TouchEditingAsync(id);
        var next = acquired.At!.Value.AddSeconds(1);
        var refresher = new ContentService(owner.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            owner.Access, new FixedClock(next));
        var other = await CreateMemberAsync(SiteRole.Editor, name: "late-takeover-editor");
        using var late = await App.UserAsync(other.Id);
        var version = (await owner.Content.GetEditAsync(id)).Version;
        var interceptor = new EditingWriteInterceptor(async ct => { await refresher.TouchEditingAsync(id, ct: ct); });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var state = await InterceptEditingWrite(late, interceptor).TouchEditingAsync(id, takeOver: true, ct: timeout.Token);
        Assert.True(interceptor.Entered);
        Assert.True(state.HeldByOther);
        Assert.Equal(ownerId, state.UserId);
        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(row.EditingAt, state.At);
        Assert.True(row.EditingAt > acquired.At);
        Assert.Equal(ownerId, row.EditingById);
        Assert.Equal(version, row.Version);
    }

    [PostgresFact]
    public async Task A_delayed_release_does_not_clear_a_refreshed_lock()
    {
        var (id, ownerId) = await CreateOwnedContentAsync("Original editor");
        using var owner = await App.UserAsync(ownerId);
        var acquired = await owner.Content.TouchEditingAsync(id);
        var refresher = new ContentService(owner.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            owner.Access, new FixedClock(acquired.At!.Value.AddSeconds(1)));
        var version = (await owner.Content.GetEditAsync(id)).Version;
        var interceptor = new EditingWriteInterceptor(async ct => { await refresher.TouchEditingAsync(id, ct: ct); });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await InterceptEditingWrite(owner, interceptor).ReleaseEditingAsync(id, timeout.Token);
        Assert.True(interceptor.Entered);
        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(ownerId, row.EditingById);
        Assert.True(row.EditingAt > acquired.At);
        Assert.Equal(version, row.Version);
    }

    [PostgresFact]
    public async Task A_heartbeat_losing_to_a_content_save_reports_the_database_lock()
    {
        var (id, ownerId) = await CreateOwnedContentAsync("Delayed editor");
        using var actor = await App.UserAsync(ownerId);
        using var editor = await App.AdministratorAsync();
        var edit = await editor.Content.GetEditAsync(id);
        var originalVersion = edit.Version;
        var interceptor = new EditingWriteInterceptor(async ct =>
        {
            edit.Title = "Concurrent content update";
            await editor.Content.UpdateAsync(edit, ct);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var state = await InterceptEditingWrite(actor, interceptor).TouchEditingAsync(id, ct: timeout.Token);
        Assert.True(interceptor.Entered);
        Assert.Null(state.UserId);
        Assert.Null(state.At);
        Assert.Equal("", state.DisplayName);
        Assert.False(state.HeldByOther);
        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Null(row.EditingById);
        Assert.Equal(edit.Version, row.Version);
        Assert.NotEqual(originalVersion, row.Version);
        Assert.Equal(edit.Title, row.Title);
        Assert.Single(await db.ContentRevisions.Where(x => x.ContentId == id).ToListAsync());
    }

    [PostgresFact]
    public async Task An_author_heartbeat_cannot_write_after_editor_publication_or_scheduling()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        var editorUser = await CreateMemberAsync(SiteRole.Editor, name: "concurrent-reviewer");
        using var actor = await App.UserAsync(author.Id);
        using var editor = await App.UserAsync(editorUser.Id);
        foreach (var status in new[] { ContentStatus.Published, ContentStatus.Draft })
        {
            var saved = await actor.Content.SaveAsync(NewContent("Concurrent review " + status, status: ContentStatus.Pending));
            var id = saved.Id!.Value;
            var edit = await editor.Content.GetEditAsync(id);
            var interceptor = new EditingWriteInterceptor(async ct =>
            {
                edit.Status = status;
                edit.PublishAt = status == ContentStatus.Draft ? DateTimeOffset.UtcNow.AddHours(1) : null;
                await editor.Content.UpdateAsync(edit, ct);
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await Assert.ThrowsAsync<AccessDeniedException>(() => InterceptEditingWrite(actor, interceptor).TouchEditingAsync(id, ct: timeout.Token));
            Assert.True(interceptor.Entered);
            await using var db = await App.CreateContextAsync();
            var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Null(row.EditingById);
            Assert.Null(row.EditingAt);
            Assert.Equal(edit.Version, row.Version);
            Assert.Equal(status, row.Status);
        }
    }

    private ContentService InterceptEditingWrite(TestActor actor, EditingWriteInterceptor interceptor) =>
        new(new InterceptingFactory(App.ConnectionString, actor.Site.SiteId, interceptor), actor.Access, TimeProvider.System);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InterceptingFactory(string connection, Guid siteId, IInterceptor interceptor) : IDbContextFactory<CmsDbContext>
    {
        public CmsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(connection, o => o.UseVector()).AddInterceptors(interceptor).Options, new SiteContext { SiteId = siteId });

        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class EditingWriteInterceptor(Func<CancellationToken, Task> beforeWrite) : DbCommandInterceptor
    {
        private int entered;
        public bool Entered => Volatile.Read(ref entered) != 0;

        private async Task BeforeWriteAsync(DbCommand command, CancellationToken ct)
        {
            if (command.CommandText.StartsWith("UPDATE \"Contents\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"EditingById\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref entered, 1) == 0)
                await beforeWrite(ct);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await BeforeWriteAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await BeforeWriteAsync(command, cancellationToken);
            return result;
        }
    }
}
