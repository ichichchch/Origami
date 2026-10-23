namespace Origami.Tests;

/// <summary>Scripted IChatClient: returns queued responses and records every model call for assertions.</summary>
public sealed class FakeChatClient : IChatClient
{
    public sealed record Call(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

    private readonly Queue<Func<Call, ChatResponse>> _script = new();
    public List<Call> Calls { get; } = [];

    public void EnqueueText(string text) =>
        Enqueue(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    public void Enqueue(Func<Call, ChatResponse> respond) => _script.Enqueue(respond);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call(messages.ToList(), options));
        if (_script.Count == 0) throw new InvalidOperationException("FakeChatClient received an unexpected model call.");
        return Task.FromResult(_script.Dequeue()(Calls[^1]));
    }

    public object? GetService(Type serviceType, object? key = null) => null;
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The assistant only uses non-streaming calls.");
    public void Dispose() { }
}

/// <summary>Hands the shared scripted client to SiteAssistant without provider configuration.</summary>
public sealed class StubAssistantChatClientFactory(FakeChatClient client) : IAssistantChatClientFactory
{
    public IChatClient Create() => client;
}
