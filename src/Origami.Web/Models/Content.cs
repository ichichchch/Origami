namespace Origami.Web.Models;

public enum ContentKind { Post, Page }
public enum ContentStatus { Draft, Published, Trashed }
public enum TaxonomyKind { Category, Tag }

public static class Limits
{
    public const int Title = 200;
    public const int Slug = 160;
    public const int Body = 200_000;
    public const int Excerpt = 500;
    public const int AltText = 300;
    public const int TaxonomyName = 80;
    public const int SiteName = 100;
    public const int Description = 500;
    public const int PageSize = 10;
    public const int MaxPageSize = 50;
    public const int MaxTaxonomiesPerKind = 30;
    public const long UploadBytes = 5 * 1024 * 1024;
    public const long ImagePixels = 20_000_000;
    public const int ImageDimension = 10_000;
    public const int EmbeddingDimensions = 1536;
    public const int EmbeddingChunkChars = 1500;
    public const int EmbeddingChunkMax = 4000;
    public const int EmbeddingBatchSize = 64;
    public const int SearchMaxResults = 20;
    public const int SearchQueryMax = 500;
    public const int FeedItems = 20;
    public const int RevisionKeep = 20;
    public const int AutosaveSeconds = 60;
    public const int TrashRetentionDays = 30;
    // A schedule must be at least this far ahead so the worker promotes it at the intended minute.
    public const int ScheduleLeadMinutes = 5;
    // Responsive widths generated next to every upload; variants wider than the source are skipped.
    public static readonly int[] ImageVariantWidths = [480, 960, 1600];
    public const int AssistantQuestionMax = 8_000;
    public const int AssistantHistoryLimit = 20;
}

public sealed class AdminUser : IdentityUser
{
    // A unique, checked constant prevents concurrent provisioning of a second administrator.
    public int SingletonKey { get; set; } = 1;
    /// <summary>Base32 TOTP shared secret; empty until two-factor setup begins.</summary>
    public string TwoFactorSecret { get; set; } = "";
    // Two-factor enablement reuses the inherited IdentityUser.TwoFactorEnabled column.
}

public sealed class Content
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ContentKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Markdown { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public ContentStatus Status { get; set; }
    // Sticky posts lead the public article list; pages ignore the flag.
    public bool IsSticky { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    /// <summary>Future publish time for a draft; the lifecycle worker promotes it when due.</summary>
    public DateTimeOffset? PublishAt { get; set; }
    /// <summary>Set while the content sits in the trash; drives the retention cleanup.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>Unguessable share link that renders this content before it is published.</summary>
    public Guid? PreviewToken { get; set; }
    /// <summary>True when the public URL requires a password before rendering the body.</summary>
    public bool IsProtected { get; set; }
    /// <summary>Identity-hashed protection password; empty when unprotected.</summary>
    public string ProtectionHash { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<ContentView> Views { get; set; } = new List<ContentView>();
}

/// <summary>Snapshot of the content as it was before a save; restore loads it back into the editor.</summary>
public sealed class ContentRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentId { get; set; }
    public string Title { get; set; } = "";
    public string Markdown { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Content Content { get; set; } = null!;
}

/// <summary>One day of view counts for one content; the only analytics surface.</summary>
public sealed class ContentView
{
    public Guid ContentId { get; set; }
    public DateOnly Date { get; set; }
    public int Count { get; set; }
    public Content Content { get; set; } = null!;
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<Content> Contents { get; set; } = new List<Content>();
}

public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<Content> Contents { get; set; } = new List<Content>();
}

public sealed class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string AltText { get; set; } = "";
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

/// <summary>Maps a retired public slug to the content that used to carry it, for permanent redirects.</summary>
public sealed class ContentSlugRedirect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OldSlug { get; set; } = "";
    public Guid ContentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Content Content { get; set; } = null!;
}

/// <summary>One chunk of a published content's text with its embedding vector.</summary>
public sealed class ContentEmbedding
{
    public Guid ContentId { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = "";
    public required Vector Embedding { get; set; }
}

/// <summary>Outbox row for the background embedding worker; deleted when the job completes.</summary>
public sealed class EmbeddingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentId { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
}

public sealed class SiteSettings
{
    public int Id { get; set; } = 1;
    [Required, StringLength(Limits.SiteName)] public string Name { get; set; } = "Origami";
    [StringLength(Limits.Description)] public string Description { get; set; } = "用文字，分享你的世界。";
    public Guid Version { get; set; }
}
