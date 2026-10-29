namespace Origami.Core.Intelligence.SemanticSearch;

public sealed record SemanticHit(Guid Id, ContentKind Kind, string Title, string Slug, double Score, string Snippet, string? ContentTypeSlug = null)
{
    public string PublicPath => ContentTypeSlug is { Length: > 0 } typeSlug
        ? $"/{ContentTypeService.PublicPrefix}/{Uri.EscapeDataString(typeSlug)}/{Uri.EscapeDataString(Slug)}"
        : Kind == ContentKind.Page ? $"/pages/{Uri.EscapeDataString(Slug)}" : $"/posts/{Uri.EscapeDataString(Slug)}";
}

public sealed class SemanticSearchService(IDbContextFactory<CmsDbContext> factory, IEmbeddingGateway gateway)
{
    /// <summary>依語意搜尋已發布文章（或全站已發布內容，含獨立頁面與自訂內容類型）。草稿、已撤回與已刪除內容都在 SQL 中過濾。</summary>
    public async Task<IReadOnlyList<SemanticHit>> SearchAsync(string query, int limit = Limits.PageSize, CancellationToken ct = default, bool includePages = false)
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
                          where (includePages || c.Kind == ContentKind.Post)
                              && c.Status == ContentStatus.Published && !c.IsPrivate && !c.IsProtected
                          orderby e.Embedding.CosineDistance(queryVector)
                          select new { c.Id, c.Kind, c.Title, c.Slug, TypeSlug = c.ContentType != null ? c.ContentType.Slug : null, e.ChunkText, Distance = e.Embedding.CosineDistance(queryVector) })
            .Take(limit * 3).ToListAsync(ct);
        return rows.GroupBy(x => x.Id)
            .Select(g => g.MinBy(x => x.Distance)!)
            .OrderBy(x => x.Distance).Take(limit)
            .Select(x => new SemanticHit(x.Id, x.Kind, x.Title, x.Slug, Math.Round(1 - x.Distance, 4), Snippet(x.ChunkText), x.TypeSlug))
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
        // 即使背景工作留下舊向量，非公開內容也不能成為匿名推薦的查詢來源。
        var chunks = await db.ContentEmbeddings.AsNoTracking().Where(x => x.ContentId == contentId &&
                db.Contents.Any(c => c.Id == x.ContentId && c.Status == ContentStatus.Published && !c.IsPrivate && !c.IsProtected))
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
                          select new { c.Id, c.Kind, c.Title, c.Slug, TypeSlug = c.ContentType != null ? c.ContentType.Slug : null, e.ChunkText, Distance = e.Embedding.CosineDistance(queryVector) })
            .Take(limit * 3).ToListAsync(ct);
        return rows.GroupBy(x => x.Id)
            .Select(g => g.MinBy(x => x.Distance)!)
            .OrderBy(x => x.Distance).Take(limit)
            .Select(x => new SemanticHit(x.Id, x.Kind, x.Title, x.Slug, Math.Round(1 - x.Distance, 4), Snippet(x.ChunkText), x.TypeSlug))
            .ToList();
    }
}
