using Origami.Core.Intelligence.Agents;

namespace Origami.Tests;

/// <summary>
/// D 批「AI 輔助寫作側欄」：五種寫作任務的輸入驗證、角色限制、模型失敗降級與
/// 產出行為。產出僅為建議文字（服務端不落地任何內容），插入由前端人工確認。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AiWritingServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private FakeChatClient Chat => App.Services.GetRequiredService<FakeChatClient>();

    [PostgresFact]
    public async Task Outline_generation_returns_suggestion_text_without_persisting()
    {
        using var admin = await App.AdministratorAsync();
        Chat.EnqueueText("## 大綱\n- 第一節\n- 第二節");
        var writing = admin.Services.GetRequiredService<AiWritingService>();

        var result = await writing.GenerateAsync(new AiWritingInput
        {
            Task = AiWritingTask.Outline,
            Title = "站點可靠性入門",
            Markdown = "既有草稿的前段…",
            Instruction = "面向初學者"
        });
        Assert.Contains("大綱", result.Text);
        // 提示含任務要求、標題與作者指示；系統指示與工具無關（寫作輔助不掛工具）。
        var call = Assert.Single(Chat.Calls);
        Assert.NotNull(call.Options?.Instructions);
        Assert.Contains("站點可靠性入門", call.Messages[^1].Text);
        Assert.Contains("面向初學者", call.Messages[^1].Text);
    }

    [PostgresFact]
    public async Task Task_inputs_are_validated_before_calling_the_model()
    {
        using var admin = await App.AdministratorAsync();
        var writing = admin.Services.GetRequiredService<AiWritingService>();

        // 各任務的必要輸入缺失時直接拒絕，不消耗模型呼叫。
        await Assert.ThrowsAsync<CmsException>(() => writing.GenerateAsync(new AiWritingInput { Task = AiWritingTask.Outline }));
        await Assert.ThrowsAsync<CmsException>(() => writing.GenerateAsync(new AiWritingInput
        {
            Task = AiWritingTask.RewriteSelection,
            Title = "標題",
            Selection = "  "
        }));
        await Assert.ThrowsAsync<CmsException>(() => writing.GenerateAsync(new AiWritingInput
        {
            Task = AiWritingTask.Continue,
            Title = "標題",
            Markdown = ""
        }));
        await Assert.ThrowsAsync<CmsException>(() => writing.GenerateAsync(new AiWritingInput { Task = (AiWritingTask)99 }));
        Assert.Empty(Chat.Calls);

        // 選取文字超限也一律拒絕。
        await Assert.ThrowsAsync<CmsException>(() => writing.GenerateAsync(new AiWritingInput
        {
            Task = AiWritingTask.RewriteSelection,
            Selection = new string('x', Limits.AiWritingInputMax + 1)
        }));
        Assert.Empty(Chat.Calls);
    }

    [PostgresFact]
    public async Task Writing_assist_requires_a_signed_in_member()
    {
        using var anonymous = App.Anonymous();
        var writing = anonymous.Services.GetRequiredService<AiWritingService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writing.GenerateAsync(new AiWritingInput
        {
            Task = AiWritingTask.Outline,
            Title = "標題"
        }));
        Assert.Empty(Chat.Calls);
    }

    [PostgresFact]
    public async Task Model_failures_degrade_to_generic_message_and_empty_reply_is_rejected()
    {
        using var admin = await App.AdministratorAsync();
        var writing = admin.Services.GetRequiredService<AiWritingService>();

        // 供應商例外轉為通用訊息，不外洩端點或金鑰。
        Chat.Enqueue(_ => throw new HttpRequestException("https://secret-endpoint.test?key=LEAKED"));
        var error = await Assert.ThrowsAsync<CmsException>(() => writing.GenerateAsync(new AiWritingInput
        {
            Task = AiWritingTask.Draft,
            Title = "草稿題目"
        }));
        Assert.DoesNotContain("secret-endpoint", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("LEAKED", error.Message, StringComparison.Ordinal);

        // 空回覆視為失敗。
        Chat.EnqueueText("   ");
        await Assert.ThrowsAsync<CmsException>(() => writing.GenerateAsync(new AiWritingInput
        {
            Task = AiWritingTask.TitleSuggestions,
            Title = "標題"
        }));
    }
}
