using Origami.Web.Intelligence.Embedding;
using Origami.Web.Models;

namespace Origami.Tests;

public sealed class MarkdownChunkerTests
{
    [Fact]
    public void Empty_markdown_yields_no_chunks() => Assert.Empty(MarkdownChunker.Chunk(""));

    [Fact]
    public void Whitespace_only_markdown_yields_no_chunks() => Assert.Empty(MarkdownChunker.Chunk(" \n\n \r\n\n\t "));

    [Fact]
    public void Small_markdown_is_one_chunk()
    {
        var chunks = MarkdownChunker.Chunk("# 標題\n\n第一段。\n\n第二段。");
        var chunk = Assert.Single(chunks);
        Assert.Contains("標題", chunk);
        Assert.Contains("第一段。", chunk);
        Assert.Contains("第二段。", chunk);
    }

    [Fact]
    public void Chunks_stay_within_bounds_and_lose_no_paragraphs()
    {
        var blocks = Enumerable.Range(0, 20).Select(i => "段落" + i + " " + new string('內', 300)).ToList();
        var markdown = string.Join("\n\n", blocks);
        var chunks = MarkdownChunker.Chunk(markdown);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= Limits.EmbeddingChunkMax));
        foreach (var block in blocks)
            Assert.Contains(block, string.Join("\n\n", chunks));
    }

    [Fact]
    public void Oversized_single_block_is_hard_split_at_the_maximum()
    {
        var oversized = new string('字', Limits.EmbeddingChunkMax * 2 + 17);
        var chunks = MarkdownChunker.Chunk(oversized);
        Assert.Equal(3, chunks.Count);
        Assert.Equal(Limits.EmbeddingChunkMax, chunks[0].Length);
        Assert.Equal(Limits.EmbeddingChunkMax, chunks[1].Length);
        Assert.Equal(17, chunks[2].Length);
    }

    [Fact]
    public void Chunking_is_deterministic()
    {
        var markdown = "# 標題\n\n" + string.Join("\n\n", Enumerable.Range(0, 10).Select(i => new string('段', 400) + i));
        Assert.Equal(MarkdownChunker.Chunk(markdown), MarkdownChunker.Chunk(markdown));
    }
}
