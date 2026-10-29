namespace Origami.Core.Services;

public sealed record PublicSearchHit(Guid Id, string Title, string Slug, string Snippet, string Source,
    ContentKind Kind = ContentKind.Post, string? ContentTypeSlug = null, string? PublicPath = null)
{
    public string ResolvedPath => !string.IsNullOrWhiteSpace(PublicPath) ? PublicPath
        : ContentTypeSlug is { Length: > 0 } typeSlug
            ? $"/{ContentTypeService.PublicPrefix}/{Uri.EscapeDataString(typeSlug)}/{Uri.EscapeDataString(Slug)}"
            : Kind == ContentKind.Page ? $"/pages/{Uri.EscapeDataString(Slug)}" : $"/posts/{Uri.EscapeDataString(Slug)}";
}

/// <summary>
/// 混合式公開搜尋：精確關鍵字比對（語意搜尋對產品名稱或錯誤代碼等精確詞彙
/// 力有未逮）與語意結果合併。沒有嵌入供應商時關鍵字比對仍可運作；
/// 語意失敗只降級，不讓整個搜尋失敗。支援 type-aware 公開路徑（文章、CPT 與可選獨立頁面）。
/// </summary>
public sealed class PublicSearchService(
    SemanticSearchService semantic,
    IDbContextFactory<CmsDbContext> factory,
    MarkdownService markdown)
{
    public sealed record Result(IReadOnlyList<PublicSearchHit> Hits, bool SemanticAvailable, int Total);

    public async Task<Result> SearchAsync(string query, int limit = Limits.PageSize, int page = 1, CancellationToken ct = default, bool includePages = false)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) throw new CmsException("請輸入搜尋文字。");
        limit = Math.Clamp(limit, 1, Limits.SearchMaxResults);
        page = Math.Max(1, page);

        var (keyword, total) = await KeywordAsync(query, limit, (page - 1) * limit, includePages, ct);
        // 語意補強僅用於第一頁：語意結果以相關度取前 N，翻頁之後本質是關鍵字檢索。
        List<SemanticHit> semanticHits = [];
        var semanticAvailable = true;
        if (page == 1)
        {
            try
            {
                // 閘道是唯一的失敗點；其任何失敗（包括缺少設定）
                // 只停用語意搜尋這一路，關鍵字結果仍照常提供。
                semanticHits = [.. await semantic.SearchAsync(query, limit, ct, includePages)];
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
        }

        var bySlug = new Dictionary<string, PublicSearchHit>();
        foreach (var hit in keyword)
        {
            var combined = semanticHits.Any(s => s.Slug == hit.Slug);
            bySlug[hit.Slug] = hit with { Source = combined ? "關鍵字＋語意" : "關鍵字" };
        }
        if (page == 1)
        {
            foreach (var hit in semanticHits.Where(hit => !bySlug.ContainsKey(hit.Slug)))
            {
                bySlug.Add(hit.Slug, new PublicSearchHit(hit.Id, hit.Title, hit.Slug, hit.Snippet, "語意",
                    hit.Kind, hit.ContentTypeSlug, hit.PublicPath));
            }
        }
        return new(bySlug.Values.Take(limit).ToList(), semanticAvailable, total);
    }

    /// <summary>對已發布內容的標題、摘要與本文進行不區分大小寫的包含比對，附總數與分頁。</summary>
    private async Task<(List<PublicSearchHit> Hits, int Total)> KeywordAsync(string query, int limit, int offset, bool includePages, CancellationToken ct)
    {
        var pattern = "%" + ContentRules.EscapeLike(query) + "%";
        await using var db = await factory.CreateDbContextAsync(ct);
        var matched = db.Contents.AsNoTracking()
            .Where(x => (includePages || x.Kind == ContentKind.Post)
                && x.Status == ContentStatus.Published && !x.IsProtected && !x.IsPrivate
                && (EF.Functions.ILike(x.Title, pattern) || EF.Functions.ILike(x.Excerpt, pattern) || EF.Functions.ILike(x.Markdown, pattern)));
        var total = await matched.CountAsync(ct);
        var rows = await matched
            .OrderByDescending(x => x.IsSticky).ThenByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(limit)
            .Select(x => new
            {
                x.Id, x.Kind, x.Title, x.Slug, x.Excerpt, x.Markdown,
                TypeSlug = x.ContentType != null ? x.ContentType.Slug : null
            }).ToListAsync(ct);
        return ([.. rows.Select(x =>
        {
            var path = x.TypeSlug is { Length: > 0 } ts
                ? $"/{ContentTypeService.PublicPrefix}/{Uri.EscapeDataString(ts)}/{Uri.EscapeDataString(x.Slug)}"
                : x.Kind == ContentKind.Page ? $"/pages/{Uri.EscapeDataString(x.Slug)}" : $"/posts/{Uri.EscapeDataString(x.Slug)}";
            return new PublicSearchHit(x.Id, x.Title, x.Slug, SnippetAround(x.Excerpt, x.Markdown, query), "關鍵字",
                x.Kind, x.TypeSlug, path);
        })], total);
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
