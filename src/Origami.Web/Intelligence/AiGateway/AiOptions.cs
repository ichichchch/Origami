namespace Origami.Web.Intelligence.AiGateway;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public EmbeddingOptions Embedding { get; set; } = new();

    public AssistantOptions Assistant { get; set; } = new();

    public sealed class EmbeddingOptions
    {
        public string Endpoint { get; set; } = "https://api.openai.com/v1/embeddings";
        public string Model { get; set; } = "text-embedding-3-small";

        /// <summary>Provided only through external configuration; never stored in source control.</summary>
        public string? ApiKey { get; set; }
    }

    public sealed class AssistantOptions
    {
        public string Endpoint { get; set; } = "https://api.openai.com/v1";
        public string Model { get; set; } = "gpt-4o-mini";

        /// <summary>Provided only through external configuration; never stored in source control.</summary>
        public string? ApiKey { get; set; }
    }
}
