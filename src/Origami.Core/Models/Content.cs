namespace Origami.Core.Models;

public enum ContentKind { Post, Page }
/// <summary>Draft 草稿；Pending 已提交待審（僅 Editor 以上可發布或退回）；Trashed 回收桶。</summary>
public enum ContentStatus { Draft, Published, Trashed, Pending }
public enum TaxonomyKind { Category, Tag }
/// <summary>媒體種類：圖片會重新編碼並產生響應式變體，其餘以原樣儲存並按種類提供。</summary>
public enum MediaKind { Image = 0, Document = 1, Audio = 2, Video = 3 }

public static partial class Limits
{
    public const int Title = 200;
    public const int Slug = 160;
    public const int Body = 200_000;
    public const int Excerpt = 500;
    public const int AltText = 300;
    public const int Caption = 300;
    /// <summary>非圖片媒體的單檔大小上限（PDF／音訊／影片）。</summary>
    public const long FileUploadBytes = 20 * 1024 * 1024;
    /// <summary>單次批次上傳的檔案數上限；單檔大小上限仍由 UploadBytes 把關。</summary>
    public const int MediaBatchMax = 20;
    public const int TaxonomyName = 80;
    /// <summary>自訂內容類型的名稱上限。</summary>
    public const int ContentTypeName = 80;
    /// <summary>自訂欄位的名稱與鍵值上限。</summary>
    public const int CustomFieldName = 80;
    /// <summary>自訂內容類型的路由代稱上限。</summary>
    public const int ContentTypeSlug = 80;
    /// <summary>自訂分類法的名稱上限。</summary>
    public const int CustomTaxonomyName = 80;
    /// <summary>版型預設名稱上限。</summary>
    public const int ThemePreset = 20;
    /// <summary>版型色碼長度上限（# 加六位數）。</summary>
    public const int ThemeColor = 7;
    /// <summary>卡片樣式名稱上限。</summary>
    public const int ThemeCardStyle = 10;
    public const int ThemeColorScheme = 10;
    public const int FooterText = 300;
    public const int SocialUrl = 300;
    public const int CommentBlockedWords = 2000;
    public const int MaintenanceMessage = 500;
    public const int CustomFieldOptions = 500;
    public const int WebhookUrl = 500;
    public const int WebhookSecret = 128;
    public const int AuditAction = 64;
    public const int AuditDetail = 500;
    public const int RecoveryCodesCount = 8;
    public const int SiteName = 100;
    public const int SiteSlug = 80;
    /// <summary>導覽項目顯示文字上限。</summary>
    public const int NavTitle = 80;
    /// <summary>自訂 meta 描述長度上限。</summary>
    public const int MetaDescription = 300;
    /// <summary>導覽外部連結網址長度上限。</summary>
    public const int NavUrl = 500;
    public const int HostName = 253;
    public const int CredentialName = 100;
    public const int SitePageSize = 50;
    public const int ApiCredentialMax = 20;
    public const int ApiCredentialDays = 365;
    public const int Description = 500;
    public const int PageSize = 10;
    public const int MaxPageSize = 50;
    public const int MaxFeedItems = 50;
    public const int TaxonomyDescription = 500;
    public const int MaxTaxonomiesPerKind = 30;
    public const int AuthorName = 80;
    public const int UserBio = 500;
    public const int UserUrl = 500;
    public const long UploadBytes = 5 * 1024 * 1024;
    /// <summary>匯出／匯入封裝（JSON 或 ZIP）的大小上限。</summary>
    public const long ArchiveMaxBytes = 256 * 1024 * 1024;
    /// <summary>主機級請求主體上限，保護匿名 /mcp 等端點；完整備份匯入端點另逐請求放寬到 <see cref="ArchiveMaxBytes"/>。</summary>
    public const long HostRequestMaxBytes = 50 * 1024 * 1024;
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
    /// <summary>編輯占用鎖的心跳有效期；逾期的占用可被他人接管，不阻止開啟編輯器。</summary>
    public const int EditingLockSeconds = 90;
    /// <summary>編輯器的心跳間隔；在占用期限內至少送出兩次才有容錯。</summary>
    public const int EditingHeartbeatSeconds = 30;
    public const int TrashRetentionDays = 30;
    // 單次批次操作的項目上限；版本逐筆攜帶，因此一次清單操作必須有界。
    public const int BulkMaxItems = 50;
    // 排程至少須提前這麼多分鐘，背景工作才能在預定的分鐘準時將其發布。
    public const int ScheduleLeadMinutes = 5;
    // 隨每次上傳產生的響應式寬度；比原始圖更寬的變體會略過。
    public static readonly int[] ImageVariantWidths = [480, 960, 1600];
    public const int AssistantQuestionMax = 8_000;
    public const int AssistantHistoryLimit = 20;
}

public sealed class AdminUser : IdentityUser
{
    // 只有 CLI 初始化的唯一全站管理者為 1；其他帳號一律由站點成員資格授權。
    public int? SingletonKey { get; set; }
    public bool IsDisabled { get; set; }
    /// <summary>Base32 編碼的 TOTP 共用密鑰；兩步驗證設定開始前為空。</summary>
    public string TwoFactorSecret { get; set; } = "";
    /// <summary>以分號分隔的 SHA-256 備用恢復碼雜湊清單；每組恢復碼使用一次即移除。</summary>
    [StringLength(1024)] public string RecoveryCodeHashes { get; set; } = "";
    // 兩步驗證的啟用重用繼承自 IdentityUser.TwoFactorEnabled 的欄位。
    /// <summary>作者公開個人簡介；顯示於作者彙整頁與文章作者欄。</summary>
    [StringLength(Limits.UserBio)] public string Bio { get; set; } = "";
    /// <summary>作者公開網站（僅 http／https）。</summary>
    [StringLength(Limits.UserUrl)] public string WebsiteUrl { get; set; } = "";
    /// <summary>作者頭像網址（僅 http／https 或站內 /media/ 圖片）。</summary>
    [StringLength(Limits.UserUrl)] public string AvatarUrl { get; set; } = "";
}

public sealed class Content
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public ContentKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Markdown { get; set; } = "";
    public string Excerpt { get; set; } = "";
    /// <summary>自訂 meta 描述（社群分享與搜尋結果摘要）；空字串時退回文章摘要。</summary>
    [StringLength(Limits.MetaDescription)] public string MetaDescription { get; set; } = "";
    /// <summary>為 true 時不納入搜尋引擎索引，也不列入 sitemap。</summary>
    public bool NoIndex { get; set; }
    public ContentStatus Status { get; set; }
    // 置頂文章排在公開文章列表最前；頁面忽略此旗標。
    public bool IsSticky { get; set; }
    /// <summary>單篇內容是否允許訪客評論；須同時滿足全站開啟評論才會顯示與接受張貼。</summary>
    public bool AllowComments { get; set; } = true;
    /// <summary>獨立頁面的同層排序權重（數字越小越靠前）；文章預設為 0。</summary>
    public int MenuOrder { get; set; }
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
    /// <summary>自訂內容類型；null 表示內建文章或頁面。</summary>
    public Guid? ContentTypeId { get; set; }
    public ContentType? ContentType { get; set; }
    /// <summary>內容的封面圖（Featured Image）；未設定時為 null。僅引用媒體資產，本文內嵌與否不影響此欄位。</summary>
    public Guid? CoverMediaId { get; set; }
    public MediaAsset? Cover { get; set; }
    /// <summary>建立者的帳號識別；帳號刪除或匯入內容時為 null，署名改由快照欄位呈現。</summary>
    public string? AuthorId { get; set; }
    public AdminUser? Author { get; set; }
    /// <summary>建立當下的作者顯示名稱快照（成員在本站的顯示名稱）；空字串時前台退回站點設定的作者名稱。</summary>
    [StringLength(Limits.AuthorName)] public string AuthorName { get; set; } = "";
    /// <summary>作者顯示名稱的正規化代稱（供作者彙整頁）；無署名時為空字串。</summary>
    [StringLength(Limits.Slug)] public string AuthorSlug { get; set; } = "";
    /// <summary>目前占用編輯器的成員帳號；無人占用或占用逾期時為 null（逾期後可被接管）。</summary>
    public string? EditingById { get; set; }
    /// <summary>占用者的顯示名稱快照；供其他編輯者的提示訊息使用。</summary>
    [StringLength(Limits.AuthorName)] public string EditingByName { get; set; } = "";
    /// <summary>占用者最近一次心跳時間；超過 Limits.EditingLockSeconds 視同無人占用。</summary>
    public DateTimeOffset? EditingAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<ContentView> Views { get; set; } = new List<ContentView>();
    public ICollection<ContentCustomTerm> CustomTerms { get; set; } = new List<ContentCustomTerm>();
    public ICollection<ContentFieldValue> FieldValues { get; set; } = new List<ContentFieldValue>();
}

/// <summary>內容儲存前的快照；還原時載回編輯器。</summary>
public sealed class ContentRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
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
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public Guid ContentId { get; set; }
    public DateOnly Date { get; set; }
    public int Count { get; set; }
    public Content Content { get; set; } = null!;
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>父分類識別；最上層分類為 null。</summary>
    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<Content> Contents { get; set; } = new List<Content>();
}

public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<Content> Contents { get; set; } = new List<Content>();
}

public sealed class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    /// <summary>媒體種類；圖片為 0，其餘為文件／音訊／影片。</summary>
    public MediaKind Kind { get; set; } = MediaKind.Image;
    /// <summary>媒體存放檔名的副檔名（決定提供時的內容類型）。</summary>
    public string FileName { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string AltText { get; set; } = "";
    /// <summary>封面或圖片說明文字；顯示於前台封面下方，可留空。</summary>
    public string Caption { get; set; } = "";
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

/// <summary>把已除役的公開 slug 對應到原本使用它的內容，用於永久轉址。</summary>
public sealed class ContentSlugRedirect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public string OldSlug { get; set; } = "";
    public Guid ContentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Content Content { get; set; } = null!;
}

/// <summary>已發布內容文字的單一切塊及其嵌入向量。</summary>
public sealed class ContentEmbedding
{
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public Guid ContentId { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = "";
    public required Vector Embedding { get; set; }
}

/// <summary>背景嵌入工作的 outbox 資料列；工作完成時刪除。</summary>
public sealed class EmbeddingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public Guid ContentId { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
}

public sealed class SiteSettings
{
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public int Id { get; set; } = 1;
    [Required, StringLength(Limits.SiteName)] public string Name { get; set; } = "Origami";
    [StringLength(Limits.Description)] public string Description { get; set; } = "用文字，分享你的世界。";
    /// <summary>公開列表每頁的文章數；null 時退回 <see cref="Limits.PageSize"/>。</summary>
    public int? PostsPerPage { get; set; }
    /// <summary>RSS feed 中的項目數；null 時退回 <see cref="Limits.FeedItems"/>。</summary>
    public int? FeedItems { get; set; }
    /// <summary>為 true 時 feed 輸出全文（content:encoded／Atom content）；null 或 false 維持摘要。</summary>
    public bool? FeedShowFullText { get; set; }
    /// <summary>文章 byline 顯示的作者名稱；空字串不顯示。</summary>
    [StringLength(Limits.AuthorName)] public string AuthorDisplayName { get; set; } = "";
    /// <summary>渲染為首頁的已發布頁面；null 時首頁維持為文章列表。</summary>
    public Guid? HomePageContentId { get; set; }
    /// <summary>啟用受控導覽；關閉時沿用「已發布頁面自動排序」的預設導覽。</summary>
    public bool CustomNavigationEnabled { get; set; }
    /// <summary>啟用訪客評論；關閉時前台不出現評論表單也不接受張貼。</summary>
    public bool CommentsEnabled { get; set; }
    /// <summary>評論自動核可；關閉時全部進入待審佇列。</summary>
    public bool CommentAutoApprove { get; set; }
    /// <summary>用作網站圖示的媒體資產；null 時不提供 favicon。</summary>
    public Guid? SiteIconMediaId { get; set; }
    /// <summary>顯示於前台頁首的網站標誌；null 時僅顯示站名文字。</summary>
    public Guid? LogoMediaId { get; set; }
    /// <summary>版型預設名稱；僅接受 <see cref="ThemePresets"/> 內建的受控預設。</summary>
    [StringLength(Limits.ThemePreset)] public string ThemePreset { get; set; } = "reading";
    /// <summary>版型強調色（十六進位色碼）。</summary>
    [StringLength(Limits.ThemeColor)] public string AccentColor { get; set; } = "#2f6f4e";
    /// <summary>正文欄寬（字元），限於可讀範圍。</summary>
    public int ReadingWidth { get; set; } = 68;
    /// <summary>是否顯示文章署名。</summary>
    public bool ShowByline { get; set; } = true;
    /// <summary>是否顯示封面圖。</summary>
    public bool ShowCover { get; set; } = true;
    /// <summary>文章卡片樣式：list 或 grid。</summary>
    [StringLength(Limits.ThemeCardStyle)] public string CardStyle { get; set; } = ThemePresets.CardStyleList;
    /// <summary>外觀色彩模式：auto（跟隨系統）、light（淺色）或 dark（深色）。</summary>
    [StringLength(Limits.ThemeColorScheme)] public string ColorScheme { get; set; } = ThemePresets.ColorSchemeAuto;
    /// <summary>自訂頁尾版權或補充說明文字；空字串時顯示預設版權。</summary>
    [StringLength(Limits.FooterText)] public string FooterText { get; set; } = "";
    /// <summary>頁尾 GitHub 連結（僅 http／https）。</summary>
    [StringLength(Limits.SocialUrl)] public string SocialGithubUrl { get; set; } = "";
    /// <summary>頁尾 X / Twitter 連結（僅 http／https）。</summary>
    [StringLength(Limits.SocialUrl)] public string SocialXUrl { get; set; } = "";
    /// <summary>頁尾 Mastodon / 社群連結（僅 http／https）。</summary>
    [StringLength(Limits.SocialUrl)] public string SocialMastodonUrl { get; set; } = "";
    /// <summary>評論敏感詞清單（每行或逗號分隔）；命中時自動轉入垃圾佇列。</summary>
    [StringLength(Limits.CommentBlockedWords)] public string CommentBlockedWords { get; set; } = "";
    /// <summary>文章發布超過指定天數後自動關閉評論；0 表示不自動關閉。</summary>
    public int CommentCloseDays { get; set; }
    /// <summary>啟用維護模式；開啟時一般訪客看到 503 維護畫面，管理員仍可正常預覽。</summary>
    public bool MaintenanceMode { get; set; }
    /// <summary>維護模式自訂提示訊息。</summary>
    [StringLength(Limits.MaintenanceMessage)] public string MaintenanceMessage { get; set; } = "";
    /// <summary>出站 Webhook 接收網址（僅 https 或本機 http）；內容發布或新評論時觸發。</summary>
    [StringLength(Limits.WebhookUrl)] public string WebhookUrl { get; set; } = "";
    /// <summary>出站 Webhook HMAC-SHA256 簽章密鑰。</summary>
    [StringLength(Limits.WebhookSecret)] public string WebhookSecret { get; set; } = "";
    public Guid Version { get; set; }
}

/// <summary>管理操作審計日誌：記錄站點重要變更與安全事件。</summary>
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    [StringLength(128)] public string UserId { get; set; } = "";
    [StringLength(256)] public string UserName { get; set; } = "";
    [Required, StringLength(Limits.AuditAction)] public string Action { get; set; } = "";
    [StringLength(Limits.AuditDetail)] public string Target { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
