namespace Origami.Tests;

/// <summary>
/// Regressions for the content lifecycle rules: the trash purge, the page hierarchy and the
/// publication dates. The race cases use gated interceptors to hold one side of the interleaving.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LifecycleRegressionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task The_purge_keeps_content_that_was_restored_meanwhile()
    {
        using var admin = await App.AdministratorAsync();
        var post = await admin.Content.SaveAsync(NewContent("回收桶競態"));
        await admin.Content.TrashAsync(post.Id!.Value, post.Version, true);
        await using (var db = await App.CreateContextAsync())
        {
            await db.Contents.Where(x => x.Id == post.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, DateTimeOffset.UtcNow.AddDays(-40)));
        }

        var gate = new DeleteGate();
        var service = new ContentService(new GatedFactory(App.ConnectionString, gate),
            admin.Services.GetRequiredService<AdminAccess>(), TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var purge = service.PurgeExpiredTrashAsync(timeout.Token);
        try
        {
            await gate.DeleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), timeout.Token);
            // The administrator restores the content while the purge is between listing and deleting.
            var trashed = await admin.Content.GetEditAsync(post.Id!.Value, timeout.Token);
            await admin.Content.RestoreAsync(trashed.Id!.Value, trashed.Version, timeout.Token);
            gate.Release.TrySetResult();
            await purge;

            await using var check = await App.CreateContextAsync();
            Assert.True(await check.Contents.AnyAsync(x => x.Id == post.Id), "a restored draft survives the purge");
            Assert.Equal(ContentStatus.Draft, (await admin.Content.GetEditAsync(post.Id!.Value)).Status);
        }
        finally
        {
            gate.Release.TrySetResult();
            if (!purge.IsCompleted) await timeout.CancelAsync();
            await purge;
        }
    }

    [PostgresFact]
    public async Task Parallel_page_saves_cannot_close_a_cycle()
    {
        using var admin = await App.AdministratorAsync();
        var a = await admin.Content.CreateAsync(ContentKind.Page, new()
        { Kind = ContentKind.Page, Title = "頁面甲", Slug = "cycle-a", Markdown = "A" });
        var b = await admin.Content.CreateAsync(ContentKind.Page, new()
        { Kind = ContentKind.Page, Title = "頁面乙", Slug = "cycle-b", Markdown = "B" });
        a.ParentId = b.Id;
        b.ParentId = a.Id;

        var access = admin.Services.GetRequiredService<AdminAccess>();
        var gate = new HierarchyGate();
        var serviceA = new ContentService(new GatedFactory(App.ConnectionString, new HierarchyLockGate(gate), new HierarchySaveGate(gate)),
            access, TimeProvider.System);
        var serviceB = new ContentService(new GatedFactory(App.ConnectionString, new HierarchyLockGate(gate), new HierarchySaveGate(gate)),
            access, TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task<ContentEdit>? taskA = null;
        Task<ContentEdit>? taskB = null;
        try
        {
            taskA = serviceA.UpdateAsync(a, timeout.Token);
            var reached = await Task.WhenAny(gate.SaveEntered.Task, taskA, Task.Delay(TimeSpan.FromSeconds(30)));
            if (ReferenceEquals(reached, taskA)) await taskA; // surfaces the real failure
            Assert.Same(gate.SaveEntered.Task, reached);
            taskB = serviceB.UpdateAsync(b, timeout.Token);
            var second = await Task.WhenAny(gate.SecondLockEntered.Task, taskB, Task.Delay(TimeSpan.FromSeconds(30)));
            if (ReferenceEquals(second, taskB)) await taskB;
            Assert.Same(gate.SecondLockEntered.Task, second);
            // The first save commits; the second one then reads the committed hierarchy.
            gate.ReleaseSave.TrySetResult();
            await taskA;
            await Assert.ThrowsAsync<CmsException>(() => taskB);
        }
        finally
        {
            gate.ReleaseSave.TrySetResult();
            if (taskA is { IsCompleted: false } || taskB is { IsCompleted: false }) await timeout.CancelAsync();
            if (taskA is not null) await Task.WhenAny(taskA, Task.Delay(TimeSpan.FromSeconds(5)));
            if (taskB is not null) await Task.WhenAny(taskB, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        await using var check = await App.CreateContextAsync();
        var savedA = await check.Contents.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        var savedB = await check.Contents.AsNoTracking().SingleAsync(x => x.Id == b.Id);
        Assert.False(savedA.ParentId == savedB.Id && savedB.ParentId == savedA.Id, "the hierarchy never becomes a cycle");
    }

    [PostgresFact]
    public async Task A_scheduled_republish_keeps_the_first_publish_date()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("排程重新發布", status: ContentStatus.Published);
        post.Slug = "republish-keeps-date";
        var saved = await admin.Content.SaveAsync(post);
        Assert.NotNull(saved.PublishedAt);
        // The stored value is read back so both sides carry the database precision.
        DateTimeOffset firstPublishedAt;
        await using (var db = await App.CreateContextAsync())
            firstPublishedAt = (await db.Contents.AsNoTracking().SingleAsync(x => x.Id == saved.Id)).PublishedAt!.Value;

        var edit = await admin.Content.GetEditAsync(saved.Id!.Value);
        edit.Status = ContentStatus.Draft;
        edit.PublishAt = DateTimeOffset.UtcNow.AddMinutes(20);
        await admin.Content.UpdateAsync(edit);

        var promoter = new ContentService(admin.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            admin.Services.GetRequiredService<AdminAccess>(), new FixedClock(DateTimeOffset.UtcNow.AddMinutes(21)));
        await promoter.PromoteDueAsync();

        var republished = await admin.Content.GetEditAsync(saved.Id!.Value);
        Assert.Equal(ContentStatus.Published, republished.Status);
        Assert.Equal(firstPublishedAt, republished.PublishedAt);
    }

    [PostgresFact]
    public async Task A_fixed_home_page_still_reaches_the_filtered_article_list()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "旅行", Slug = "travel" });
        var post = NewContent("分類文章", status: ContentStatus.Published);
        post.Slug = "category-post";
        post.CategoryIds = [category.Id!.Value];
        await admin.Content.SaveAsync(post);
        var home = await admin.Content.CreateAsync(ContentKind.Page, new()
        { Kind = ContentKind.Page, Title = "固定首頁", Slug = "fixed-home", Status = ContentStatus.Published, Markdown = "HOME_CONTENT" });
        var settings = await admin.Settings.GetAsync();
        settings.HomePageContentId = home.Id;
        await admin.Settings.SaveAsync(settings);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var homeHtml = WebUtility.HtmlDecode(await (await client.GetAsync("/")).Content.ReadAsStringAsync());
        Assert.Contains("HOME_CONTENT", homeHtml);
        Assert.DoesNotContain("分類文章", homeHtml);

        var filtered = WebUtility.HtmlDecode(await (await client.GetAsync("/?category=travel")).Content.ReadAsStringAsync());
        Assert.Contains("分類文章", filtered);
        Assert.DoesNotContain("HOME_CONTENT", filtered);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class GatedFactory(string connection, params IInterceptor[] interceptors) : IDbContextFactory<CmsDbContext>
    {
        public CmsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(connection, o => o.UseVector()).AddInterceptors(interceptors).Options);

        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>Holds the purge between the expired listing and the delete.</summary>
    private sealed class DeleteGate : DbCommandInterceptor
    {
        public TaskCompletionSource DeleteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM \"Contents\"", StringComparison.Ordinal))
            {
                DeleteEntered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    /// <summary>Reports the hierarchy lock and holds the first save before it writes.</summary>
    private sealed class HierarchyGate
    {
        private int lockCalls;
        public TaskCompletionSource FirstLockEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondLockEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int NextLock() => Interlocked.Increment(ref lockCalls);
    }

    private sealed class HierarchyLockGate(HierarchyGate gate) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Signal(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Signal(command);
            return ValueTask.FromResult(result);
        }

        private void Signal(DbCommand command)
        {
            if (!command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) return;
            if (gate.NextLock() == 1) gate.FirstLockEntered.TrySetResult();
            else gate.SecondLockEntered.TrySetResult();
        }
    }

    private sealed class HierarchySaveGate(HierarchyGate gate) : SaveChangesInterceptor
    {
        private int saveCalls;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref saveCalls) == 1)
            {
                gate.SaveEntered.TrySetResult();
                await gate.ReleaseSave.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
