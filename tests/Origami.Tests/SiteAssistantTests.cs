using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Origami.Web.Intelligence.Agents;
using Origami.Web.Intelligence.AiGateway;
using Origami.Web.Models;
using Origami.Web.Services;

namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SiteAssistantTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Anonymous_calls_are_rejected_before_the_model_is_contacted()
    {
        using var visitor = App.Anonymous();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => visitor.Assistant.AskAsync([], "有什麼文章？"));
        Assert.Empty(App.Chat.Calls);
    }

    [PostgresFact]
    public async Task Empty_and_overlong_questions_are_rejected_without_model_calls()
    {
        using var admin = await App.AdministratorAsync();
        await Assert.ThrowsAsync<CmsException>(() => admin.Assistant.AskAsync([], "   "));
        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Assistant.AskAsync([], new string('長', Limits.AssistantQuestionMax + 1)));
        Assert.Empty(App.Chat.Calls);
    }

    [PostgresFact]
    public async Task Reply_text_is_returned_and_bounded_history_is_replayed_with_the_new_question()
    {
        using var admin = await App.AdministratorAsync();
        App.Chat.EnqueueText("  第一答  ");
        Assert.Equal("第一答", await admin.Assistant.AskAsync([], "第一問"));

        App.Chat.EnqueueText("第二答");
        var history = new List<AssistantMessage>
        {
            new(true, "舊一"), new(false, "舊答一"), new(true, "舊二"), new(false, "舊答二"),
            new(true, "第一問"), new(false, "第一答"),
        };
        Assert.Equal("第二答", await admin.Assistant.AskAsync(history, "第二問"));

        var call = App.Chat.Calls[^1];
        var turns = call.Messages.Where(m => m.Role != ChatRole.System).Select(m => m.Text).ToArray();
        Assert.Equal(new[] { "舊一", "舊答一", "舊二", "舊答二", "第一問", "第一答", "第二問" }, turns);
        Assert.Equal(ChatRole.User, call.Messages[^1].Role);
    }

    [PostgresFact]
    public async Task Read_only_tools_and_instructions_are_declared_to_the_model()
    {
        using var admin = await App.AdministratorAsync();
        App.Chat.EnqueueText("好");
        await admin.Assistant.AskAsync([], "嗨");

        var call = App.Chat.Calls.Single();
        Assert.NotNull(call.Options);
        Assert.Contains("Origami", call.Options.Instructions);
        var names = (call.Options.Tools ?? [])
            .Select(tool => tool is AIFunction function ? function.Name : tool.ToString() ?? "")
            .Where(name => name.Length > 0)
            .Order().ToArray();
        Assert.Equal(new[] { "get_page", "get_post", "search_posts" }, names);
    }

    [PostgresFact]
    public async Task Model_requested_search_executes_the_read_only_tool_and_feeds_the_result_back()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Content.SaveAsync(NewContent("Origami 部署指南", status: ContentStatus.Published));
        await App.Worker.ProcessPendingAsync();

        App.Chat.Enqueue(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            (IList<AIContent>)[new FunctionCallContent("call-1", "search_posts",
                new Dictionary<string, object?> { ["query"] = "部署" })])));
        App.Chat.EnqueueText("有一篇《Origami 部署指南》。");

        var reply = await admin.Assistant.AskAsync([], "網站有部署相關的文章嗎？");

        Assert.Equal("有一篇《Origami 部署指南》。", reply);
        Assert.Equal(2, App.Chat.Calls.Count);
        var result = App.Chat.Calls[1].Messages.SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>().Single(r => r.CallId == "call-1");
        // The framework JSON-encodes the tool result with non-ASCII escaping, so assert structurally.
        var payload = result.Result as string ?? JsonSerializer.Serialize(result.Result);
        var found = System.Text.Json.Nodes.JsonNode.Parse(payload)?.AsArray()
            .Any(hit => hit?["title"]?.GetValue<string>()?.Contains("部署指南") == true) == true;
        Assert.True(found, $"search_posts result did not include the published post: {payload}");
    }
}

public sealed class AssistantChatClientFactoryTests
{
    [Fact]
    public void Missing_key_is_a_friendly_error()
    {
        var factory = new AssistantChatClientFactory(Monitor(_ => { }));
        var error = Assert.Throws<CmsException>(() => factory.Create());
        Assert.Contains("尚未設定", error.Message);
    }

    [Fact]
    public void Configured_settings_produce_a_stable_client()
    {
        var factory = new AssistantChatClientFactory(Monitor(options =>
        {
            options.Assistant.ApiKey = "test-key";
            options.Assistant.Model = "test-model";
        }));
        Assert.NotNull(factory.Create());
        Assert.Same(factory.Create(), factory.Create());
    }

    private static IOptionsMonitor<AiOptions> Monitor(Action<AiOptions> configure)
    {
        var options = new AiOptions();
        configure(options);
        return new OptionsMonitor<AiOptions>(
            new OptionsFactory<AiOptions>([new ConfigureOptions<AiOptions>(configure)], []),
            [], new OptionsCache<AiOptions>());
    }
}
