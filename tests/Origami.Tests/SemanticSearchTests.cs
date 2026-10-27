namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SemanticSearchTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Search_hits_published_posts_only_and_ranks_by_similarity()
    {
        // 正交基底向量使相似度精確：貓文案為 e0，狗文案為 e1。
        App.Embeddings.VectorFor = text => FakeEmbeddingGateway.Basis(text.Contains("狗") ? 1 : 0);
        using var actor = await App.AdministratorAsync();
        await actor.Content.SaveAsync(Published("貓的生活", "# 貓\n\n貓貓喜歡睡覺。"));
        await actor.Content.SaveAsync(Published("狗的日子", "# 狗\n\n狗狗喜歡散步。"));
        await actor.Content.SaveAsync(NewContent("貓的草稿")); // 草稿：不排任何工作，永不嵌入
        await App.Worker.ProcessPendingAsync();

        var hits = await actor.Semantic.SearchAsync("我要找貓");
        Assert.NotEmpty(hits);
        // 前 k 筆搜尋可能包含弱匹配；精確匹配必須以高分排名第一。
        Assert.Equal("貓的生活", hits[0].Title);
        Assert.True(hits[0].Score > 0.9, $"預期高分，實際 {hits[0].Score}");
        Assert.All(hits, hit => Assert.Contains(hit.Title, new[] { "貓的生活", "狗的日子" }));

        // 草稿的殘留向量絕不能外洩：SQL 的發布過濾是最後一道防線。
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
    public async Task Search_validates_before_gateway_and_accepts_the_exact_query_bound()
    {
        using var actor = await App.AdministratorAsync();
        foreach (var invalid in new[] { null, "", " \t\r\n", new string('x', Limits.SearchQueryMax + 1),
                     " " + new string('x', Limits.SearchQueryMax) })
        {
            await Assert.ThrowsAsync<CmsException>(() => actor.Semantic.SearchAsync(invalid!));
            Assert.Equal(0, App.Embeddings.EmbeddedTexts);
        }
        var boundary = new string('x', Limits.SearchQueryMax);
        string? sent = null;
        App.Embeddings.VectorFor = text => { sent = text; return FakeEmbeddingGateway.Basis(0); };
        var hits = await actor.Semantic.SearchAsync(boundary, 999);
        Assert.Empty(hits); // 前 k 筆數值被夾至上限而非拋例外；空語料庫回傳空結果
        Assert.Equal(boundary, sent);
        Assert.Equal(1, App.Embeddings.EmbeddedTexts);
        await actor.Semantic.SearchAsync("  主題  ");
        Assert.Equal("主題", sent);
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
