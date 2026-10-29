namespace Origami.Core.Intelligence.Agents;

public sealed record AssistantMessage(bool FromUser, string Text);

/// <summary>
/// 以 Microsoft Agent Framework 組裝的後台對話助理。其 function tools 與 MCP 端點
/// 暴露的唯讀已發布內容查詢相同，因此助理與外部模型共用同一組工具，
/// 永遠不會取得寫入權限。
/// </summary>
public sealed class SiteAssistant(
    IAssistantChatClientFactory clients,
    McpReadTools tools,
    AdminAccess admin)
{
    private const string Instructions = """
        你是 Origami 網站的內容助理，服務對象是網站管理員。
        回答與網站內容相關的問題時，先使用提供的工具搜尋已發布的文章或頁面，依工具結果回答，並提及文章標題或網址代稱。
        工具查不到就明確說查不到，不要編造內容；你只能看到已發布內容，看不到草稿或已撤回的內容。
        你沒有建立、修改、發布或刪除內容的能力；被要求時請說明這些操作需由管理員手動完成。
        以繁體中文回答，簡潔清楚。
        """;

    public async Task<string> AskAsync(IReadOnlyList<AssistantMessage> conversation, string question, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        question = (question ?? "").Trim();
        if (question.Length == 0) throw new CmsException("請輸入問題。");
        if (question.Length > Limits.AssistantQuestionMax)
            throw new CmsException($"問題長度上限為 {Limits.AssistantQuestionMax} 字。");
        // 只重放最近的有界視窗；較舊的對話輪次不會進入提示。
        var messages = conversation.TakeLast(Limits.AssistantHistoryLimit)
            .Select(turn => new ChatMessage(turn.FromUser ? ChatRole.User : ChatRole.Assistant, turn.Text))
            .Append(new ChatMessage(ChatRole.User, question))
            .ToList();
        var agent = new ChatClientAgent(clients.Create(), new ChatClientAgentOptions
        {
            Name = "OrigamiAssistant",
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                // 與 MCP 端點共用的唯讀工具（名稱與範圍相同）；不存在任何寫入工具。
                Tools =
                [
                    AIFunctionFactory.Create(tools.SearchPostsAsync, name: "search_posts",
                        description: "以語意搜尋本站已發布的文章，回傳標題、網址、相關分數與內容摘要。"),
                    AIFunctionFactory.Create(tools.GetPostAsync, name: "get_post",
                        description: "以網址代稱（slug）取得單篇已發布文章，含完整 Markdown、分類與標籤。"),
                    AIFunctionFactory.Create(tools.GetPageAsync, name: "get_page",
                        description: "以網址代稱（slug）取得已發布的獨立頁面，含完整 Markdown。"),
                ],
            },
        });
        try
        {
            var response = await agent.RunAsync(messages, cancellationToken: ct);
            var text = response.Text?.Trim();
            if (string.IsNullOrEmpty(text)) throw new CmsException("助理沒有回覆內容，請稍後再試。");
            return text;
        }
        catch (Exception error) when (error is not CmsException and not OperationCanceledException)
        {
            // 供應商失敗保持通用訊息：端點、金鑰或回應內容都不外洩到 UI。
            throw new CmsException("助理暫時無法回覆，請稍後再試。");
        }
    }

    /// <summary>
    /// 編輯器寫作輔助：依據目前標題與 Markdown 正文，產生精簡摘要（Excerpt）與 SEO 描述（MetaDescription）。
    /// 若 AI 供應商未設定或暫時無法回應，自動退回本地確定性純文字摘錄，確保編輯流程不中斷。
    /// </summary>
    public async Task<AiContentSuggestion> SuggestSummaryAsync(string? title, string? markdown, MarkdownService markdownService, CancellationToken ct = default)
    {
        await admin.RequireNonCredentialUserAsync(ct);
        await admin.RequireRoleAsync(SiteRole.Author, ct);
        var cleanTitle = (title ?? "").Trim();
        var cleanBody = (markdown ?? "").Trim();
        if (cleanTitle.Length == 0 && cleanBody.Length == 0)
            throw new CmsException("請先輸入標題或正文內容，再產生摘要建議。");

        var fallbackExcerpt = markdownService.Excerpt(cleanBody.Length > 0 ? cleanBody : cleanTitle);
        var fallbackMeta = fallbackExcerpt.Length <= Limits.MetaDescription ? fallbackExcerpt : fallbackExcerpt[..(Limits.MetaDescription - 1)].TrimEnd() + "…";

        try
        {
            var client = clients.Create();
            var snippet = cleanBody.Length > 4000 ? cleanBody[..4000] : cleanBody;
            var prompt = $"""
                請根據以下文章標題與正文，分別撰寫：
                1. 文章摘要（Excerpt，最多 160 字，吸引讀者閱讀）
                2. SEO 描述（MetaDescription，最多 120 字，適合搜尋引擎結果摘要）
                請只回傳兩行純文字，第一行為 Excerpt，第二行為 MetaDescription，不要包含標籤或編號。

                標題：{cleanTitle}
                正文：
                {snippet}
                """;
            var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], cancellationToken: ct);
            var lines = (response.Text ?? "")
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (lines.Count >= 1)
            {
                var ex = lines[0];
                if (ex.Length > Limits.Excerpt) ex = ex[..Limits.Excerpt];
                var meta = lines.Count >= 2 ? lines[1] : ex;
                if (meta.Length > Limits.MetaDescription) meta = meta[..Limits.MetaDescription];
                return new AiContentSuggestion(ex, meta);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 未設定 AI 或呼叫失敗時退回本地摘要產生。
        }
        return new AiContentSuggestion(fallbackExcerpt, fallbackMeta);
    }
}
