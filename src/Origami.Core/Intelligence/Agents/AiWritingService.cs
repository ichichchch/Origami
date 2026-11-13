namespace Origami.Core.Intelligence.Agents;

/// <summary>編輯器側欄的寫作輔助任務；產出僅為建議，插入與否由作者人工確認。</summary>
public enum AiWritingTask { Outline, Draft, RewriteSelection, Continue, TitleSuggestions }

/// <summary>寫作輔助輸入：依任務組合題目、選取文字、本文脈絡與補充指示。</summary>
public sealed class AiWritingInput
{
    public AiWritingTask Task { get; set; }
    [StringLength(Limits.Title)] public string Title { get; set; } = "";
    /// <summary>改寫任務的選取文字；其他任務可作為草稿生成的大綱。</summary>
    public string? Selection { get; set; }
    /// <summary>目前 Markdown 本文（大綱與續寫的脈絡來源）；服務端截斷，不全文送出。</summary>
    public string? Markdown { get; set; }
    /// <summary>作者補充指示（選填）：語氣、受眾、重點等。</summary>
    [StringLength(2000)] public string? Instruction { get; set; }
}

public sealed record AiWritingResult(string Text);

/// <summary>
/// 編輯器側欄的 AI 寫作輔助：大綱、草稿、改寫選取、續寫與標題建議。
/// 產出只是建議文字，由作者在預覽後自行決定插入或取代，服務端不落地任何內容；
/// 角色限 Author 以上，模型端點與金鑰沿用 Ai:Assistant 外部設定。
/// </summary>
public sealed class AiWritingService(IAssistantChatClientFactory clients, AdminAccess admin)
{
    private const string Instructions = """
        你是寫作助理，協助網站作者產生與打磨 Markdown 文稿。
        只輸出 Markdown 內容本身（可用清單、標題、粗體等語法），不使用原始 HTML，不加程式碼圍欄外的說明文字。
        不編造無法從給定材料得知的具體事實；需要資料時以「（待補）」標注。
        以與來稿相同的語言書寫；無法判斷時以繁體中文書寫。
        """;

    private const int ContextChars = 4000;

    public async Task<AiWritingResult> GenerateAsync(AiWritingInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await admin.RequireRoleAsync(SiteRole.Author, ct);
        var title = (input.Title ?? "").Trim();
        var selection = (input.Selection ?? "").Trim();
        var markdown = (input.Markdown ?? "").Trim();
        var instruction = (input.Instruction ?? "").Trim();
        if (selection.Length > Limits.AiWritingInputMax) throw new CmsException($"選取文字上限為 {Limits.AiWritingInputMax} 字。");
        if (markdown.Length > Limits.Body) throw new CmsException($"本文長度超過上限 {Limits.Body} 字。");

        string prompt;
        switch (input.Task)
        {
            case AiWritingTask.Outline:
                if (title.Length == 0) throw new CmsException("請先輸入標題，再生成大綱。");
                prompt = BuildPrompt("大綱", title, selection, markdown, instruction,
                    "請產生這篇文章的階層式大綱（二至三層，Markdown 標題與清單）。");
                break;
            case AiWritingTask.Draft:
                if (title.Length == 0 && selection.Length == 0) throw new CmsException("請先輸入標題或大綱，再生成草稿。");
                prompt = BuildPrompt("草稿", title, selection, markdown, instruction,
                    "請撰寫完整的文章草稿；若提供大綱則按大綱展開。");
                break;
            case AiWritingTask.RewriteSelection:
                if (selection.Length == 0) throw new CmsException("請先在編輯器選取要改寫的文字。");
                prompt = BuildPrompt("改寫", title, selection, markdown, instruction,
                    "請改寫「選取文字」段落，保持原意並提升清晰度；只輸出改寫後的段落。");
                break;
            case AiWritingTask.Continue:
                if (markdown.Length == 0) throw new CmsException("本文為空，無法續寫；請先寫下開頭。");
                prompt = BuildPrompt("續寫", title, selection, markdown, instruction,
                    "請順著正文的最後段落繼續寫下去，銜接語氣與風格；只輸出續寫內容。", tailContext: true);
                break;
            case AiWritingTask.TitleSuggestions:
                if (title.Length == 0 && markdown.Length == 0) throw new CmsException("請先輸入標題或正文，再建議標題。");
                prompt = BuildPrompt("標題建議", title, selection, markdown, instruction,
                    "請提出 5 個標題建議，每行一個，不編號。");
                break;
            default:
                throw new CmsException("不支援的寫作輔助任務。");
        }

        try
        {
            var client = clients.Create();
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { Instructions = Instructions }, ct);
            var text = (response.Text ?? "").Trim();
            if (text.Length == 0) throw new CmsException("模型沒有回覆內容，請稍後再試。");
            if (text.Length > Limits.AiWritingOutputMax) text = text[..Limits.AiWritingOutputMax];
            return new AiWritingResult(text);
        }
        catch (Exception error) when (error is not CmsException and not OperationCanceledException)
        {
            // 供應商失敗保持通用訊息：端點、金鑰或回應內容都不外洩到 UI。
            throw new CmsException("寫作輔助暫時無法回覆，請稍後再試。");
        }
    }

    /// <summary>組裝任務提示；正文脈絡取前段（或續寫時取後段），避免把整篇本文塞進提示。</summary>
    private static string BuildPrompt(string taskName, string title, string selection, string markdown, string instruction,
        string requirement, bool tailContext = false)
    {
        var context = markdown.Length switch
        {
            0 => "",
            <= ContextChars => markdown,
            _ when tailContext => "…（前略）" + markdown[^ContextChars..],
            _ => markdown[..ContextChars] + "…（後略）",
        };
        var parts = new List<string> { $"任務：{requirement}" };
        if (title.Length > 0) parts.Add($"標題：{title}");
        if (selection.Length > 0) parts.Add(taskName == "草稿" ? $"大綱：\n{selection}" : $"選取文字：\n{selection}");
        if (context.Length > 0) parts.Add($"正文：\n{context}");
        if (instruction.Length > 0) parts.Add($"作者指示：{instruction}");
        return string.Join("\n\n", parts);
    }
}
