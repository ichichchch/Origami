using Microsoft.EntityFrameworkCore;
using Origami.Web.Models;
using Origami.Web.Services;
using Pgvector;

namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SemanticSearchTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Search_hits_published_posts_only_and_ranks_by_similarity()
    {
        // Orthogonal basis vectors make similarity exact: cat texts are e0, dog texts are e1.
        App.Embeddings.VectorFor = text => FakeEmbeddingGateway.Basis(text.Contains("狗") ? 1 : 0);
        using var actor = await App.AdministratorAsync();
        await actor.Content.SaveAsync(Published("貓的生活", "# 貓\n\n貓貓喜歡睡覺。"));
        await actor.Content.SaveAsync(Published("狗的日子", "# 狗\n\n狗狗喜歡散步。"));
        await actor.Content.SaveAsync(NewContent("貓的草稿")); // draft: queued for nothing, never embedded
        await App.Worker.ProcessPendingAsync();

        var hits = await actor.Semantic.SearchAsync("我要找貓");
        Assert.NotEmpty(hits);
        // Top-k search may include weak matches; the exact match must rank first with a high score.
        Assert.Equal("貓的生活", hits[0].Title);
        Assert.True(hits[0].Score > 0.9, $"預期高分，實際 {hits[0].Score}");
        Assert.All(hits, hit => Assert.Contains(hit.Title, new[] { "貓的生活", "狗的日子" }));

        // A stale vector for a draft must never leak: the SQL publish filter is the last line of defense.
        var draft = await FindContentIdAsync("貓的草稿");
        await using (var db = await App.CreateContextAsync())
        {
            db.ContentEmbeddings.Add(new ContentEmbedding { ContentId = draft, ChunkIndex = 0, ChunkText = "貓貓秘密草稿", Embedding = new Vector(FakeEmbeddingGateway.Basis(0)) });
            await db.SaveChangesAsync();
        }
        hits = await actor.Semantic.SearchAsync("我要找貓");
        Assert.Equal("貓的生活", hits[0].Title);
        Assert.DoesNotContain(hits, hit => hit.Title == "貓的草稿");
    }

    [PostgresFact]
    public async Task Search_rejects_blank_queries_and_clamps_limits()
    {
        using var actor = await App.AdministratorAsync();
        await Assert.ThrowsAsync<CmsException>(() => actor.Semantic.SearchAsync("   "));
        var hits = await actor.Semantic.SearchAsync("隨便", 999);
        Assert.Empty(hits); // clamped, not thrown; an empty corpus returns nothing
    }

    [PostgresFact]
    public async Task Pages_are_not_part_of_post_search()
    {
        App.Embeddings.VectorFor = _ => FakeEmbeddingGateway.Basis(0);
        using var actor = await App.AdministratorAsync();
        await actor.Content.SaveAsync(Published("只有文章", "文章本文"));
        await actor.Content.SaveAsync(new ContentEdit { Title = "獨立頁面", Kind = ContentKind.Page, Status = ContentStatus.Published, Markdown = "頁面本文" });
        await App.Worker.ProcessPendingAsync();
        var hits = await actor.Semantic.SearchAsync("本文");
        Assert.Equal("只有文章", Assert.Single(hits).Title);
    }

    private static ContentEdit Published(string title, string markdown) =>
        new() { Title = title, Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = markdown };

    private async Task<Guid> FindContentIdAsync(string title)
    {
        await using var db = await App.CreateContextAsync();
        return await db.Contents.AsNoTracking().Where(x => x.Title == title).Select(x => x.Id).SingleAsync();
    }
}
