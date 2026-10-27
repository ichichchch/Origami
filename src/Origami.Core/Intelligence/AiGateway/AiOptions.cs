namespace Origami.Core.Intelligence.AiGateway;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public EmbeddingOptions Embedding { get; set; } = new();

    public AssistantOptions Assistant { get; set; } = new();

    public sealed class EmbeddingOptions
    {
        public string Endpoint { get; set; } = "https://api.openai.com/v1/embeddings";
        public string Model { get; set; } = "text-embedding-3-small";

        /// <summary>僅由外部設定提供；絕不存入原始碼版控。</summary>
        public string? ApiKey { get; set; }
    }

    public sealed class AssistantOptions
    {
        public string Endpoint { get; set; } = "https://api.openai.com/v1";
        public string Model { get; set; } = "gpt-4o-mini";

        /// <summary>僅由外部設定提供；絕不存入原始碼版控。</summary>
        public string? ApiKey { get; set; }
    }
}
