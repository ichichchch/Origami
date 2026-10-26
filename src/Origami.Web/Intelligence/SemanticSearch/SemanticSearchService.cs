namespace Origami.Web.Intelligence.SemanticSearch;

public sealed record SemanticHit(Guid Id, ContentKind Kind, string Title, string Slug, double Score, string Snippet);

public sealed class SemanticSearchService(IDbContextFactory<CmsDbContext> factory, IEmbeddingGateway gateway)
{
    /// <summary>Searches published posts by meaning. Drafts, withdrawn and deleted content are filtered in SQL.</summary>
    public async Task<IReadOnlyList<SemanticHit>> SearchAsync(string query, int limit = Limits.PageSize, CancellationToken ct = default)
    {
        if (query?.Length > Limits.SearchQueryMax)
            throw new CmsException($"搜尋文字上限為 {Limits.SearchQueryMax} 字。");
        query = (query ?? "").Trim();
        if (query.Length == 0) throw new CmsException("請輸入搜尋文字。");
        limit = Math.Clamp(limit, 1, Limits.SearchMaxResults);
        var queryVector = new Vector(await gateway.EmbedQueryAsync(query, ct));
        await using var db = await factory.CreateDbContextAsync(ct);
        // The publish filter is part of the SQL, never a post-hoc memory filter.
        var rows = await (from e in db.ContentEmbeddings.AsNoTracking()
                          join c in db.Contents.AsNoTracking() on e.ContentId equals c.Id
                          where c.Kind == ContentKind.Post && c.Status == ContentStatus.Published
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
}
