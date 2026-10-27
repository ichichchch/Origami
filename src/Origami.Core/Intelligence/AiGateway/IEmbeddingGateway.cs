namespace Origami.Core.Intelligence.AiGateway;

/// <summary>Vendor-neutral access to an embedding model, isolated behind the AiGateway boundary.</summary>
public interface IEmbeddingGateway
{
    Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default);

    /// <summary>Embeds many texts, preserving input order; batching is handled internally.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
