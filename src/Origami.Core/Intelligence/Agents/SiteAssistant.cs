namespace Origami.Core.Intelligence.Agents;

public sealed record AssistantMessage(bool FromUser, string Text);

/// <summary>
/// Admin-facing conversational assistant assembled with Microsoft Agent Framework. Its function tools
/// are the same read-only published-content queries the MCP endpoint exposes, so the assistant shares
/// one tool set with external models and never gains write access.
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
        // Only the recent bounded window is replayed; older turns fall out of the prompt.
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
                // Read-only tools shared with the MCP endpoint (same names and scope); no write tools exist.
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
            // Provider failures stay generic: no endpoint, key, or response body leaks into the UI.
            throw new CmsException("助理暫時無法回覆，請稍後再試。");
        }
    }
}
