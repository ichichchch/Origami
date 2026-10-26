namespace Origami.Web.Intelligence.Agents;

public sealed record McpContentResult(bool Found, PublicContent? Content);

/// <summary>
/// Read-only MCP tools exposing published content to models. There are deliberately no
/// write tools here: state changes require human confirmation and are out of scope for MCP.
/// </summary>
[McpServerToolType]
public sealed class McpReadTools(SemanticSearchService search, ContentService content)
{
    [McpServerTool(Name = "search_posts"), Description("以語意搜尋本站已發布的文章，回傳標題、網址、相關分數與內容摘要。")]
    public async Task<IReadOnlyList<SemanticHit>> SearchPostsAsync(
        [Description("搜尋文字，例如主題或問題")] string query,
        [Description("最多回傳筆數，1 到 20")] int limit = 10,
        CancellationToken ct = default)
        => await search.SearchAsync(query, limit, ct);

    [McpServerTool(Name = "get_post"), Description("以網址代稱（slug）取得單篇已發布文章，含完整 Markdown、分類與標籤。")]
    public async Task<McpContentResult> GetPostAsync(
        [Description("文章的網址代稱")] string slug, CancellationToken ct = default)
        => await GetAsync(ContentKind.Post, slug, ct);

    [McpServerTool(Name = "get_page"), Description("以網址代稱（slug）取得已發布的獨立頁面，含完整 Markdown。")]
    public async Task<McpContentResult> GetPageAsync(
        [Description("頁面的網址代稱")] string slug, CancellationToken ct = default)
        => await GetAsync(ContentKind.Page, slug, ct);

    private async Task<McpContentResult> GetAsync(ContentKind kind, string slug, CancellationToken ct)
    {
        var found = await content.GetPublishedAsync(kind, slug, ct);
        // Password-protected bodies are gated for humans and private content is admin-only;
        // a model tool must not become a side door to either.
        return new(found is not null && !found.IsProtected && !found.IsPrivate,
            found is { IsProtected: false, IsPrivate: false } ? found : null);
    }
}
