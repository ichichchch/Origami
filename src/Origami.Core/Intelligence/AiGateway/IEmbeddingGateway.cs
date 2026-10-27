namespace Origami.Core.Intelligence.AiGateway;

/// <summary>與供應商無關的嵌入模型存取，隔離於 AiGateway 邊界之後。</summary>
public interface IEmbeddingGateway
{
    Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default);

    /// <summary>嵌入多段文字並保留輸入順序；分批在內部處理。</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
