namespace Origami.Web.Intelligence.AiGateway;

/// <summary>Seam for tests: lets the assistant run against a fake IChatClient without provider configuration.</summary>
public interface IAssistantChatClientFactory
{
    IChatClient Create();
}

/// <summary>
/// Builds the assistant chat client from an OpenAI-compatible endpoint. The API key exists only in
/// the outbound Authorization header of the underlying client; it never enters source control or logs.
/// </summary>
public sealed class AssistantChatClientFactory(IOptionsMonitor<AiOptions> options) : IAssistantChatClientFactory
{
    private readonly object _gate = new();
    private IChatClient? _client;
    private (string Endpoint, string Model, string Key) _built;

    public IChatClient Create()
    {
        var settings = options.CurrentValue.Assistant;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Endpoint)
            || string.IsNullOrWhiteSpace(settings.Model))
            throw new CmsException("助理尚未設定對話模型，請管理員透過外部設定提供 Ai:Assistant 金鑰。");
        var config = (settings.Endpoint, settings.Model, settings.ApiKey);
        lock (_gate)
        {
            // Rebuild only when external configuration actually changes; the client itself is reused.
            if (_client is not null && _built == config) return _client;
            // Chat Completions keeps every OpenAI-compatible endpoint usable (the Responses API is not).
            var client = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(settings.ApiKey),
                    new OpenAI.OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) })
                .GetChatClient(settings.Model)
                .AsIChatClient();
            _client = client;
            _built = config;
            return client;
        }
    }
}
