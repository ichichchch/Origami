namespace Origami.Core.Intelligence.AiGateway;

/// <summary>呼叫 OpenAI 相容的 /embeddings 端點；是唯一知道供應商傳輸格式的地方。</summary>
public sealed partial class OpenAiEmbeddingGateway(
    HttpClient client, IOptionsMonitor<AiOptions> options, ILogger<OpenAiEmbeddingGateway> logger) : IEmbeddingGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default) =>
        (await EmbedCoreAsync([text], ct))[0];

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]>(texts.Count);
        for (var start = 0; start < texts.Count; start += Limits.EmbeddingBatchSize)
            results.AddRange(await EmbedCoreAsync(texts.Skip(start).Take(Limits.EmbeddingBatchSize).ToList(), ct));
        return results;
    }

    private async Task<List<float[]>> EmbedCoreAsync(IReadOnlyList<string> batch, CancellationToken ct)
    {
        var settings = options.CurrentValue.Embedding;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Endpoint))
            throw new CmsException("語意功能尚未設定嵌入模型，請管理員透過外部設定提供 Ai:Embedding 金鑰。");

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = JsonContent.Create(new EmbeddingRequest(settings.Model, batch), options: Json),
        };
        // 金鑰只存在於外送標頭，絕不出現在日誌或例外。
        request.Headers.Authorization = new("Bearer", settings.ApiKey);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.EmbeddingResponseRejected(logger, (int)response.StatusCode, batch.Count);
            throw new CmsException("嵌入服務暫時無法使用，請稍後再試。");
        }
        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(Json, ct);
        var items = payload?.Data;
        if (items is null || items.Count != batch.Count || items.Any(x => x.Embedding is null))
            throw new CmsException("嵌入服務回應不完整，請稍後再試。");
        foreach (var item in items)
            if (item.Embedding!.Length != Limits.EmbeddingDimensions)
                throw new CmsException("嵌入模型維度與系統設定不符，請檢查 Ai:Embedding:Model。");
        return items.OrderBy(x => x.Index).Select(x => x.Embedding!).ToList();
    }

    private sealed record EmbeddingRequest(string Model, IReadOnlyList<string> Input);
    private sealed record EmbeddingResponse(List<EmbeddingItem>? Data);
    private sealed record EmbeddingItem(int Index, float[]? Embedding);

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Warning, "嵌入服務回應 {Status}，本批 {Count} 段文字未嵌入。")]
        public static partial void EmbeddingResponseRejected(ILogger logger, int status, int count);
    }
}
