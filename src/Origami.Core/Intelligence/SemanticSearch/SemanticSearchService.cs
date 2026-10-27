namespace Origami.Core.Intelligence.SemanticSearch;

public sealed record SemanticHit(Guid Id, ContentKind Kind, string Title, string Slug, double Score, string Snippet);

public sealed class SemanticSearchService(IDbContextFactory<CmsDbContext> factory, IEmbeddingGateway gateway)
{
    /// <summary>依語意搜尋已發布文章。草稿、已撤回與已刪除內容都在 SQL 中過濾。</summary>
    public async Task<IReadOnlyList<SemanticHit>> SearchAsync(string query, int limit = Limits.PageSize, CancellationToken ct = default)
    {
        if (query?.Length > Limits.SearchQueryMax)
            throw new CmsException($"搜尋文字上限為 {Limits.SearchQueryMax} 字。");
        query = (query ?? "").Trim();
        if (query.Length == 0) throw new CmsException("請輸入搜尋文字。");
        limit = Math.Clamp(limit, 1, Limits.SearchMaxResults);
        var queryVector = new Vector(await gateway.EmbedQueryAsync(query, ct));
        await using var db = await factory.CreateDbContextAsync(ct);
        // 發布過濾條件是 SQL 的一部分，絕不是事後的記憶體過濾。
        var rows = await (from e in db.ContentEmbeddings.AsNoTracking()
                          join c in db.Contents.AsNoTracking() on e.ContentId equals c.Id
                          where c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate
                          orderby e.Embedding.CosineDistance(queryVector)
                          select new { c.Id, c.Kind, c.Title, c.Slug, e.ChunkText, Distance = e.Embedding.CosineDistance(queryVector) })
            .Take(limit * 3).ToListAsync(ct);
        return rows.GroupBy(x => x.Id)
            .Select(g => g.MinBy(x => x.Distance)!)
            .OrderBy(x => x.Distance).Take(limit)
            .Select(x => new SemanticHit(x.Id, x.Kind, x.Title, x.Slug, Math.Round(1 - x.Distance, 4), Snippet(x.ChunkText)))
            .ToList();
    }

    private static string Snippet(string text) => text.Length <= 200 ? text : text[..200] + "…";

    /// <summary>
    /// 取得單篇文章的相關已發布文章，以自身切塊向量的質心（centroid）計算餘弦相似度排序。
    /// 私密與密碼保護文章絕不出現在推薦中。
    /// </summary>
    public async Task<IReadOnlyList<SemanticHit>> RelatedAsync(Guid contentId, int limit = 3, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, Limits.SearchMaxResults);
        await using var db = await factory.CreateDbContextAsync(ct);
        var chunks = await db.ContentEmbeddings.AsNoTracking().Where(x => x.ContentId == contentId)
            .Select(x => x.Embedding).ToListAsync(ct);
        if (chunks.Count == 0) return [];
        var dimensions = Limits.EmbeddingDimensions;
        var centroid = new float[dimensions];
        foreach (var chunk in chunks)
        {
            var values = chunk.ToArray();
            for (var i = 0; i < values.Length && i < dimensions; i++) centroid[i] += values[i];
        }
        for (var i = 0; i < dimensions; i++) centroid[i] /= chunks.Count;
        var queryVector = new Vector(centroid);
        var rows = await (from e in db.ContentEmbeddings.AsNoTracking()
                          join c in db.Contents.AsNoTracking() on e.ContentId equals c.Id
                          where c.Id != contentId && c.Kind == ContentKind.Post && c.Status == ContentStatus.Published
                              && !c.IsPrivate && !c.IsProtected
                          orderby e.Embedding.CosineDistance(queryVector)
                          select new { c.Id, c.Kind, c.Title, c.Slug, e.ChunkText, Distance = e.Embedding.CosineDistance(queryVector) })
            .Take(limit * 3).ToListAsync(ct);
        return rows.GroupBy(x => x.Id)
            .Select(g => g.MinBy(x => x.Distance)!)
            .OrderBy(x => x.Distance).Take(limit)
            .Select(x => new SemanticHit(x.Id, x.Kind, x.Title, x.Slug, Math.Round(1 - x.Distance, 4), Snippet(x.ChunkText)))
            .ToList();
    }
}
