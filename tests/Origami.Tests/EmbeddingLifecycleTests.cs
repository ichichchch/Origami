namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class EmbeddingLifecycleTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Publishing_queues_work_and_the_worker_writes_vectors()
    {
        using var actor = await App.AdministratorAsync();
        var saved = await actor.Content.SaveAsync(Published("長文", string.Join("\n\n", Enumerable.Range(0, 8).Select(i => "段落" + i + new string('字', 400)))));
        Assert.True(await HasJobAsync(saved.Id!.Value));
        await App.Worker.ProcessPendingAsync();
        Assert.Equal(3, await EmbeddingCountAsync(saved.Id!.Value)); // 8 paragraphs × ~400 chars pack into 3 chunks
        Assert.False(await HasJobAsync(saved.Id!.Value));
    }

    [PostgresFact]
    public async Task Withdrawing_removes_vectors_and_jobs_in_the_same_save()
    {
        using var actor = await App.AdministratorAsync();
        var saved = await actor.Content.SaveAsync(Published("會被撤回", "內容"));
        await App.Worker.ProcessPendingAsync();
        Assert.Equal(1, await EmbeddingCountAsync(saved.Id!.Value));

        var edit = await actor.Content.GetEditAsync(saved.Id!.Value);
        edit.Status = ContentStatus.Draft;
        await actor.Content.SaveAsync(edit);

        // Synchronous, in-transaction removal: no worker pass needed before the content is invisible.
        Assert.Equal(0, await EmbeddingCountAsync(saved.Id!.Value));
        Assert.False(await HasJobAsync(saved.Id!.Value));
    }

    [PostgresFact]
    public async Task Editing_published_content_requeues_and_replaces_all_chunks()
    {
        using var actor = await App.AdministratorAsync();
        var saved = await actor.Content.SaveAsync(Published("原文", "短"));
        await App.Worker.ProcessPendingAsync();
        Assert.Equal(1, await EmbeddingCountAsync(saved.Id!.Value));

        var edit = await actor.Content.GetEditAsync(saved.Id!.Value);
        edit.Markdown = string.Join("\n\n", Enumerable.Range(0, 6).Select(i => "新段落" + i + new string('好', 400)));
        await actor.Content.SaveAsync(edit);
        await App.Worker.ProcessPendingAsync();

        Assert.Equal(2, await EmbeddingCountAsync(saved.Id!.Value));
        Assert.False(await HasJobAsync(saved.Id!.Value));
    }

    [PostgresFact]
    public async Task Provider_failure_backs_off_and_the_job_survives_to_succeed_later()
    {
        App.Embeddings.FailNext = true;
        using var actor = await App.AdministratorAsync();
        var saved = await actor.Content.SaveAsync(Published("重試文", "內容"));
        Assert.Equal(0, await App.Worker.ProcessPendingAsync());
        await using (var db = await App.CreateContextAsync())
        {
            var job = await db.EmbeddingJobs.AsNoTracking().SingleAsync(x => x.ContentId == saved.Id);
            Assert.Equal(1, job.Attempts);
            Assert.True(job.NextAttemptAt > DateTimeOffset.UtcNow);
        }
        App.Embeddings.FailNext = false;
        // The backoff delay has not elapsed yet, so the worker correctly skips the not-due job.
        Assert.Equal(0, await App.Worker.ProcessPendingAsync());
        await using (var db = await App.CreateContextAsync())
        {
            var job = await db.EmbeddingJobs.SingleAsync(x => x.ContentId == saved.Id);
            job.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await App.Worker.ProcessPendingAsync());
        Assert.Equal(1, await EmbeddingCountAsync(saved.Id!.Value));
    }

    [PostgresFact]
    public async Task Deleting_content_cascades_embeddings_and_jobs()
    {
        using var actor = await App.AdministratorAsync();
        var saved = await actor.Content.SaveAsync(Published("會被刪除", "內容"));
        await App.Worker.ProcessPendingAsync();
        await actor.Content.DeleteAsync(saved.Id!.Value, saved.Version, confirmed: true);
        Assert.Equal(0, await EmbeddingCountAsync(saved.Id!.Value));
        Assert.False(await HasJobAsync(saved.Id!.Value));
    }

    private static ContentEdit Published(string title, string markdown) =>
        new() { Title = title, Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = markdown };

    private async Task<int> EmbeddingCountAsync(Guid contentId)
    {
        await using var db = await App.CreateContextAsync();
        return await db.ContentEmbeddings.AsNoTracking().CountAsync(x => x.ContentId == contentId);
    }

    private async Task<bool> HasJobAsync(Guid contentId)
    {
        await using var db = await App.CreateContextAsync();
        return await db.EmbeddingJobs.AsNoTracking().AnyAsync(x => x.ContentId == contentId);
    }
}
