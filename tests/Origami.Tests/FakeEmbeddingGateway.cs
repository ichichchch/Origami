namespace Origami.Tests;

/// <summary>Deterministic embedding stand-in: maps texts to orthogonal basis vectors so similarity is exact.</summary>
public sealed class FakeEmbeddingGateway : IEmbeddingGateway
{
    public bool FailNext { get; set; }
    public int EmbeddedTexts { get; private set; }
    public Func<string, float[]> VectorFor { get; set; } = text => Basis(text.Length % Limits.EmbeddingDimensions);

    public static float[] Basis(int index)
    {
        var vector = new float[Limits.EmbeddingDimensions];
        vector[index] = 1;
        return vector;
    }

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default) => Task.FromResult(Embed(text));

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

    private float[] Embed(string text)
    {
        if (FailNext) throw new InvalidOperationException("fake gateway failure");
        EmbeddedTexts++;
        return VectorFor(text);
    }
}
