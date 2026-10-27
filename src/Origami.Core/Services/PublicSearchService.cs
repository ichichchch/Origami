namespace Origami.Core.Services;

public sealed record PublicSearchHit(Guid Id, string Title, string Slug, string Snippet, string Source);

/// <summary>
/// 混合式公開搜尋：精確關鍵字比對（語意搜尋對產品名稱或錯誤代碼等精確詞彙
/// 力有未逮）與語意結果合併。沒有嵌入供應商時關鍵字比對仍可運作；
/// 語意失敗只降級，不讓整個搜尋失敗。
/// </summary>
public sealed class PublicSearchService(
    SemanticSearchService semantic,
    IDbContextFactory<CmsDbContext> factory,
    MarkdownService markdown)
{
    public sealed record Result(IReadOnlyList<PublicSearchHit> Hits, bool SemanticAvailable);

    public async Task<Result> SearchAsync(string query, int limit = Limits.PageSize, CancellationToken ct = default)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) throw new CmsException("請輸入搜尋文字。");
        limit = Math.Clamp(limit, 1, Limits.SearchMaxResults);

        var keyword = await KeywordAsync(query, limit, ct);
        List<SemanticHit> semanticHits = [];
        var semanticAvailable = true;
        try
        {
            // 閘道是唯一的失敗點；其任何失敗（包括缺少設定）
            // 只停用語意搜尋這一路，關鍵字結果仍照常提供。
            semanticHits = [.. await semantic.SearchAsync(query, limit, ct)];
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 供應商逾時視為該路失敗，而非訪客取消：關鍵字結果仍照常提供。
            semanticAvailable = false;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            semanticAvailable = false;
        }

        var bySlug = new Dictionary<string, PublicSearchHit>();
        foreach (var hit in keyword)
            bySlug[hit.Slug] = new(hit.Id, hit.Title, hit.Slug, hit.Snippet, semanticHits.Any(s => s.Slug == hit.Slug) ? "關鍵字＋語意" : "關鍵字");
        foreach (var hit in semanticHits.Where(hit => !bySlug.ContainsKey(hit.Slug)))
            bySlug.Add(hit.Slug, new PublicSearchHit(hit.Id, hit.Title, hit.Slug, hit.Snippet, "語意"));
        return new(bySlug.Values.Take(limit).ToList(), semanticAvailable);
    }

    /// <summary>對已發布文章的標題、摘要與本文進行不區分大小寫的包含比對。</summary>
    private async Task<List<PublicSearchHit>> KeywordAsync(string query, int limit, CancellationToken ct)
    {
        var pattern = "%" + ContentRules.EscapeLike(query) + "%";
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Contents.AsNoTracking()
            .Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published && !x.IsProtected && !x.IsPrivate
                && (EF.Functions.ILike(x.Title, pattern) || EF.Functions.ILike(x.Excerpt, pattern) || EF.Functions.ILike(x.Markdown, pattern)))
            .OrderByDescending(x => x.IsSticky).ThenByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Take(limit).Select(x => new { x.Id, x.Title, x.Slug, x.Excerpt, x.Markdown }).ToListAsync(ct);
        return [.. rows.Select(x => new PublicSearchHit(x.Id, x.Title, x.Slug, SnippetAround(x.Excerpt, x.Markdown, query), "關鍵字"))];
    }

    private string SnippetAround(string excerpt, string body, string query)
    {
        var plain = excerpt != "" ? excerpt : markdown.PlainText(body);
        var index = plain.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return Truncate(plain);
        var start = Math.Max(0, index - 60);
        var length = Math.Min(plain.Length - start, query.Length + 120);
        return (start > 0 ? "…" : "") + plain.Substring(start, length).Trim() + (start + length < plain.Length ? "…" : "");
    }

    private static string Truncate(string text) => text.Length <= 180 ? text : text[..179].TrimEnd() + "…";
}
