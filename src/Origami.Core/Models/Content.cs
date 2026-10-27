namespace Origami.Core.Models;

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
    public const int MaxFeedItems = 50;
    public const int TaxonomyDescription = 500;
    public const int MaxTaxonomiesPerKind = 30;
    public const long UploadBytes = 5 * 1024 * 1024;
    public const long ImportMaxBytes = 50 * 1024 * 1024;
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
    // 排程至少須提前這麼多分鐘，背景工作才能在預定的分鐘準時將其發布。
    public const int ScheduleLeadMinutes = 5;
    // 隨每次上傳產生的響應式寬度；比原始圖更寬的變體會略過。
    public static readonly int[] ImageVariantWidths = [480, 960, 1600];
    public const int AssistantQuestionMax = 8_000;
    public const int AssistantHistoryLimit = 20;
}

public sealed class AdminUser : IdentityUser
{
    // 唯一且受檢查約束的常數，防止並發建立第二位管理員。
    public int SingletonKey { get; set; } = 1;
    /// <summary>Base32 編碼的 TOTP 共用密鑰；兩步驗證設定開始前為空。</summary>
    public string TwoFactorSecret { get; set; } = "";
    // 兩步驗證的啟用重用繼承自 IdentityUser.TwoFactorEnabled 的欄位。
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
    // 置頂文章排在公開文章列表最前；頁面忽略此旗標。
    public bool IsSticky { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    /// <summary>草稿的未來發布時間；到期時由生命週期背景工作發布。</summary>
    public DateTimeOffset? PublishAt { get; set; }
    /// <summary>內容待在回收桶時設定；用於驅動保留期限清理。</summary>
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>無法猜測的分享連結，用於在內容發布前渲染檢視。</summary>
    public Guid? PreviewToken { get; set; }
    /// <summary>為 true 時，公開網址需先輸入密碼才渲染內文。</summary>
    public bool IsProtected { get; set; }
    /// <summary>以 Identity 雜湊的保護密碼；未受保護時為空。</summary>
    public string ProtectionHash { get; set; } = "";
    /// <summary>為 true 時，僅已登入的管理員可讀取公開網址；其他人一律得到 404。</summary>
    public bool IsPrivate { get; set; }
    /// <summary>階層導覽用的父頁面；最上層頁面為 null，文章則一律為 null。</summary>
    public Guid? ParentId { get; set; }
    public Content? Parent { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<ContentView> Views { get; set; } = new List<ContentView>();
}

/// <summary>內容儲存前的快照；還原時載回編輯器。</summary>
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

/// <summary>單一內容單日的瀏覽次數；為唯一的分析數據。</summary>
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
    public string Description { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<Content> Contents { get; set; } = new List<Content>();
}

public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
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

/// <summary>把已除役的公開 slug 對應到原本使用它的內容，用於永久轉址。</summary>
public sealed class ContentSlugRedirect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OldSlug { get; set; } = "";
    public Guid ContentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Content Content { get; set; } = null!;
}

/// <summary>已發布內容文字的單一切塊及其嵌入向量。</summary>
public sealed class ContentEmbedding
{
    public Guid ContentId { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = "";
    public required Vector Embedding { get; set; }
}

/// <summary>背景嵌入工作的 outbox 資料列；工作完成時刪除。</summary>
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
    /// <summary>公開列表每頁的文章數；null 時退回 <see cref="Limits.PageSize"/>。</summary>
    public int? PostsPerPage { get; set; }
    /// <summary>RSS feed 中的項目數；null 時退回 <see cref="Limits.FeedItems"/>。</summary>
    public int? FeedItems { get; set; }
    /// <summary>渲染為首頁的已發布頁面；null 時首頁維持為文章列表。</summary>
    public Guid? HomePageContentId { get; set; }
    /// <summary>用作網站圖示的媒體資產；null 時不提供 favicon。</summary>
    public Guid? SiteIconMediaId { get; set; }
    public Guid Version { get; set; }
}
