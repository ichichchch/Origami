namespace Origami.Core.Intelligence.Agents;

public sealed record McpContentResult(bool Found, PublicContent? Content);

/// <summary>
/// 把已發布內容暴露給模型的唯讀 MCP 工具。這裡刻意不提供
/// 寫入工具：狀態變更須經人類確認，不屬於 MCP 的範圍。
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
        // 密碼保護的內文對人類也設有門檻，私密內容更僅限管理員；
        // 模型工具不得成為其中任何一者的側門。
        return new(found is not null && !found.IsProtected && !found.IsPrivate,
            found is { IsProtected: false, IsPrivate: false } ? found : null);
    }
}
