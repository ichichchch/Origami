using Origami.Web.Models;

namespace Origami.Web.Intelligence.Embedding;

/// <summary>Splits Markdown into embeddable chunks at paragraph boundaries, deterministic and bounded.</summary>
public static class MarkdownChunker
{
    /// <summary>Chunks never exceed Limits.EmbeddingChunkMax so they fit the storage column and model input limit.</summary>
    public static List<string> Chunk(string markdown)
    {
        var chunks = new List<string>();
        var current = new List<string>();
        var length = 0;
        foreach (var block in markdown.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var piece = block.Trim();
            if (piece.Length == 0) continue;
            if (piece.Length > Limits.EmbeddingChunkMax)
            {
                Flush();
                for (var start = 0; start < piece.Length; start += Limits.EmbeddingChunkMax)
                    chunks.Add(piece.Substring(start, Math.Min(Limits.EmbeddingChunkMax, piece.Length - start)));
                continue;
            }
            if (length > 0 && length + piece.Length + 1 > Limits.EmbeddingChunkChars) Flush();
            current.Add(piece);
            length += piece.Length + 1;
        }
        Flush();
        return chunks;

        void Flush()
        {
            if (current.Count > 0)
            {
                chunks.Add(string.Join("\n\n", current));
                current.Clear();
                length = 0;
            }
        }
    }
}
