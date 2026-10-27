namespace Origami.Core.Intelligence.AiGateway;

/// <summary>供測試使用的接縫：讓助理無需供應商設定即可對假的 IChatClient 執行。</summary>
public interface IAssistantChatClientFactory
{
    IChatClient Create();
}

/// <summary>
/// 由 OpenAI 相容端點建立助理對話用戶端。API 金鑰只存在於底層用戶端的
/// 外送 Authorization 標頭，絕不進入原始碼版控或日誌。
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
            // 只有外部設定實際變更時才重建；用戶端本身會重複使用。
            if (_client is not null && _built == config) return _client;
            // 採用 Chat Completions 讓每個 OpenAI 相容端點皆可用（Responses API 則不然）。
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
