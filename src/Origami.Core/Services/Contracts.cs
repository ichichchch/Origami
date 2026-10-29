namespace Origami.Core.Services;

public sealed class CmsException(string message) : Exception(message);

/// <summary>自訂欄位的一組值；欄位識別由伺服器驗證屬於該內容類型。</summary>
public sealed record FieldValueInput(Guid FieldId, string Value);

/// <summary>公開呈現的自訂欄位值；欄位名稱與種類隨內容一併輸出。</summary>
public sealed record PublicFieldValue(string Name, string Key, string Value, string FieldType);

public sealed class ContentEdit
{
    public Guid? Id { get; set; }
    public ContentKind Kind { get; set; }
    [Required, StringLength(Limits.Title)] public string Title { get; set; } = "";
    [StringLength(Limits.Slug)] public string? Slug { get; set; }
    [StringLength(Limits.Body)] public string? Markdown { get; set; }
    [StringLength(Limits.Excerpt)] public string? Excerpt { get; set; }
    public bool IsSticky { get; set; }
    /// <summary>單篇內容是否允許訪客評論（預設為 true）。</summary>
    public bool AllowComments { get; set; } = true;
    /// <summary>獨立頁面的同層排序權重（數字越小越靠前）。</summary>
    public int MenuOrder { get; set; }
    /// <summary>公開頁面渲染本文前須先輸入密碼。</summary>
    public bool IsProtected { get; set; }
    /// <summary>公開網址僅限已登入管理員；其他人一律得到 404。</summary>
    public bool IsPrivate { get; set; }
    /// <summary>導覽階層的上層頁面；僅頁面可用，文章一律保持 null。</summary>
    public Guid? ParentId { get; set; }
    /// <summary>封面圖片的媒體識別；null 表示未設定封面。</summary>
    public Guid? CoverMediaId { get; set; }
    /// <summary>已儲存封面檔名的唯讀回報；未設定時為空字串。</summary>
    public string CoverFileName { get; set; } = "";
    /// <summary>已儲存封面替代文字的唯讀回報。</summary>
    public string CoverAltText { get; set; } = "";
    /// <summary>已儲存封面說明文字的唯讀回報。</summary>
    public string CoverCaption { get; set; } = "";
    /// <summary>設定新的保護密碼；空字串保留已儲存者。未變更時為 null。</summary>
    public string? NewProtectPassword { get; set; }
    /// <summary>唯讀回報：已儲存保護密碼。</summary>
    public bool HasProtectPassword { get; set; }
    /// <summary>已儲存瀏覽總數的唯讀回報。</summary>
    public int Views { get; set; }
    /// <summary>唯讀回報：作者顯示名稱快照；由伺服器於建立時寫入，更新不重寫。</summary>
    public string AuthorName { get; set; } = "";
    /// <summary>自訂 meta 描述；空字串時使用文章摘要。</summary>
    [StringLength(Limits.MetaDescription)] public string MetaDescription { get; set; } = "";
    /// <summary>是否排除搜尋引擎索引。</summary>
    public bool NoIndex { get; set; }
    /// <summary>唯讀回報：建立者的帳號識別；作者角色以此判斷能否編輯。</summary>
    public string? AuthorId { get; set; }
    /// <summary>唯讀回報：目前有其他成員占用編輯器（心跳仍在有效期內）。</summary>
    public bool EditingByOther { get; set; }
    /// <summary>唯讀回報：占用者的顯示名稱；無占用時為空字串。</summary>
    public string EditingByName { get; set; } = "";
    /// <summary>未來的發布時間；僅在內容維持草稿時有意義。</summary>
    public DateTimeOffset? PublishAt { get; set; }
    public Guid? PreviewToken { get; set; }
    /// <summary>已儲存首次發布時間的唯讀回報。</summary>
    public DateTimeOffset? PublishedAt { get; set; }
    public ContentStatus Status { get; set; }
    public Guid Version { get; set; }
    /// <summary>自訂內容類型；null 表示內建文章或頁面。既有內容的類型不可變更。</summary>
    public Guid? ContentTypeId { get; set; }
    /// <summary>指派的自訂詞彙；只接受屬於本內容類型之分類法的詞彙。</summary>
    public List<Guid> CustomTermIds { get; set; } = [];
    /// <summary>自訂欄位的值；欄位必須屬於本內容類型。</summary>
    public List<FieldValueInput> FieldValues { get; set; } = [];
    public List<Guid> CategoryIds { get; set; } = [];
    public List<Guid> TagIds { get; set; } = [];
}

public sealed record TaxonomyEdit
{
    public Guid? Id { get; set; }
    [Required, StringLength(Limits.TaxonomyName)] public string Name { get; set; } = "";
    [StringLength(Limits.Slug)] public string Slug { get; set; } = "";
    [StringLength(Limits.TaxonomyDescription)] public string? Description { get; set; }
    /// <summary>父分類識別；標籤固定為 null。</summary>
    public Guid? ParentId { get; set; }
    public Guid Version { get; set; }
}

/// <summary>內容清單的批次操作種類。</summary>
public enum BulkAction { Trash, Restore, Delete }
/// <summary>批次操作的單筆結果；失敗時附原因，單筆失敗不影響其餘項目。</summary>
public sealed record BulkItemResult(Guid Id, bool Succeeded, string Message);

public sealed record ContentSummary(Guid Id, ContentKind Kind, string Title, string Slug,
    ContentStatus Status, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, Guid Version)
{
    public string Excerpt { get; init; } = "";
    // 手動摘要依原樣渲染；自動產生的前綴仍需剝離 Markdown。
    public bool ExcerptIsManual { get; init; }
    public bool IsSticky { get; init; }
    public DateTimeOffset? PublishAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public bool IsProtected { get; init; }
    public bool IsPrivate { get; init; }
    public bool AllowComments { get; init; } = true;
    public int MenuOrder { get; init; }
    public Guid? ParentId { get; init; }
    public string ParentTitle { get; init; } = "";
    public int Views { get; init; }
    public string CoverFileName { get; init; } = "";
    public string CoverAltText { get; init; } = "";
    /// <summary>封面網址；未設定封面時為 null。</summary>
    public string? CoverUrl => CoverFileName.Length == 0 ? null : "/media/" + CoverFileName;
    /// <summary>作者顯示名稱快照；空字串時前台退回站點設定的作者名稱。</summary>
    public string AuthorName { get; init; } = "";
    /// <summary>作者彙整頁的連結代稱；無署名時為空字串。</summary>
    public string AuthorSlug { get; init; } = "";
    /// <summary>自訂 meta 描述；空字串時使用文章摘要。</summary>
    public string MetaDescription { get; init; } = "";
    /// <summary>是否排除搜尋引擎索引。</summary>
    public bool NoIndex { get; init; }
    /// <summary>作者角色清單判斷編輯權限用的帳號識別。</summary>
    public string? AuthorId { get; init; }
}
public sealed record ContentCounts(int Drafts, int Published, int Pending = 0, int Trashed = 0)
{
    // 預設分頁列出草稿與已發布內容；回收桶另有專屬分頁，待審由清單狀態篩選。
    public int Total => Drafts + Published;
}
/// <summary>修訂版本列表項目；本文於需要時載入。</summary>
public sealed record RevisionItem(Guid Id, string Title, DateTimeOffset CreatedAt);
public sealed record RevisionDetail(Guid Id, Guid ContentId, string Title, string Markdown, string Excerpt, DateTimeOffset CreatedAt);
public sealed record TaxonomyItem(Guid Id, string Name, string Slug, Guid Version)
{
    public string Description { get; init; } = "";
    public Guid? ParentId { get; init; }
    public string ParentName { get; init; } = "";
}
/// <summary>帶作者描述的公開分類或標籤，供篩選列表使用。</summary>
public sealed record TaxonomyView(string Title, string Slug, string Description = "")
{
    public Guid? ParentId { get; init; }
    public string? ParentSlug { get; init; }
    public string? ParentTitle { get; init; }
}
public sealed record PublicTaxonomy(string Name, string Slug, int Count);
public sealed record PublicContent(Guid Id, ContentKind Kind, string Title, string Slug, string Markdown,
    DateTimeOffset? PublishedAt, IReadOnlyList<NavigationItem> Categories, IReadOnlyList<NavigationItem> Tags)
{
    public string Excerpt { get; init; } = "";
    public bool IsProtected { get; init; }
    public bool IsPrivate { get; init; }
    public bool AllowComments { get; init; } = true;
    public int MenuOrder { get; init; }
    public string CoverFileName { get; init; } = "";
    public string CoverAltText { get; init; } = "";
    public string CoverCaption { get; init; } = "";
    /// <summary>封面網址；未設定封面時為 null。</summary>
    public string? CoverUrl => CoverFileName.Length == 0 ? null : "/media/" + CoverFileName;
    /// <summary>作者顯示名稱快照；空字串時前台退回站點設定的作者名稱。</summary>
    public string AuthorName { get; init; } = "";
    /// <summary>作者彙整頁的連結代稱；無署名時為空字串。</summary>
    public string AuthorSlug { get; init; } = "";
    /// <summary>自訂 meta 描述；空字串時使用文章摘要。</summary>
    public string MetaDescription { get; init; } = "";
    /// <summary>是否排除搜尋引擎索引。</summary>
    public bool NoIndex { get; init; }
    /// <summary>最後更新時間；供結構化資料使用。</summary>
    public DateTimeOffset UpdatedAt { get; init; }
    /// <summary>自訂欄位的公開值；依欄位宣告順序排列。</summary>
    public IReadOnlyList<PublicFieldValue> Fields { get; init; } = [];
}
public sealed record NavigationItem(string Title, string Slug);
/// <summary>以路徑解析的已發布頁面；Segments 為根到本頁的完整 slug 鏈（未含 /pages/ 前綴）。</summary>
public sealed record PagePathContent(PublicContent Content, IReadOnlyList<string> Segments);
/// <summary>公開導覽中的已發布頁面，附階層指標。</summary>
public sealed record PageNavItem(Guid Id, string Title, string Slug, Guid? ParentId, string? Url = null, bool IsExternal = false);
/// <summary>受控導覽項目的管理端檢視。</summary>
public sealed record NavItemView(Guid Id, string Title, Guid? ContentId, string Url, bool IsVisible, int Position, Guid Version);
/// <summary>編輯器與網站設定中選擇器的頁面候選。</summary>
public sealed record PageOption(Guid Id, string Title, Guid? ParentId, ContentStatus Status,
    bool IsPrivate = false, bool IsProtected = false);
/// <summary>sitemap 的單一公開網址，附上次重大變更時間。</summary>
public sealed record SitemapItem(string Path, DateTimeOffset? LastModified);
public sealed record MediaItem(Guid Id, string FileName, string OriginalName, string ContentType,
    long Size, int Width, int Height, DateTimeOffset UploadedAt)
{
    public string AltText { get; init; } = "";
    /// <summary>封面或圖片說明文字；顯示於前台封面下方。</summary>
    public string Caption { get; init; } = "";
    /// <summary>媒體種類：圖片、文件、音訊或影片。</summary>
    public MediaKind Kind { get; init; } = MediaKind.Image;
    /// <summary>由列表填入；有多少內容在任何狀態下內嵌此圖片。</summary>
    public int ReferenceCount { get; set; }
    public string Url => "/media/" + FileName;
    // 未設定替代文字時沿用歷史預留字；括號會被逸出，作者文字無法破壞語法。
    public string Markdown => Kind == MediaKind.Image
        ? "![" + (AltText == "" ? "圖片" : AltText.Replace("[", "\\[").Replace("]", "\\]")) + "](" + Url + ")"
        : "[" + (AltText == "" ? OriginalName : AltText.Replace("[", "\\[").Replace("]", "\\]")) + "](" + Url + ")";
}
/// <summary>批次上傳的單檔結果；失敗時附原因，單檔失敗不影響其他檔案。</summary>
public sealed record MediaBatchItem(string FileName, bool Succeeded, string Message, MediaItem? Item);
/// <summary>退役 slug 背後現行的公開目標，供永久轉址使用。</summary>
public sealed record SlugRedirectTarget(string Slug, ContentKind Kind);
/// <summary>後台重定向規則檢視。</summary>
public sealed record RedirectItem(Guid Id, string OldSlug, Guid ContentId, string ContentTitle, string ContentSlug, ContentKind ContentKind, DateTimeOffset CreatedAt);
/// <summary>新增自訂重定向規則輸入。</summary>
public sealed record RedirectCreateInput(string OldSlug, Guid ContentId);
/// <summary>作者公開個人資料。</summary>
public sealed record PublicAuthorProfile(string DisplayName, string Slug, string Bio, string WebsiteUrl, string AvatarUrl);
/// <summary>帳號自助個人資料與顯示名稱。</summary>
public sealed class AccountProfileEdit
{
    [StringLength(Limits.AuthorName)] public string DisplayName { get; set; } = "";
    [StringLength(Limits.UserBio)] public string Bio { get; set; } = "";
    [StringLength(Limits.UserUrl)] public string WebsiteUrl { get; set; } = "";
    [StringLength(Limits.UserUrl)] public string AvatarUrl { get; set; } = "";
    [StringLength(256)] public string Email { get; set; } = "";
    public string UserName { get; set; } = "";
}
/// <summary>帳號自助修改密碼輸入。</summary>
public sealed class ChangePasswordInput
{
    [Required, StringLength(1024)] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(1024)] public string NewPassword { get; set; } = "";
}
/// <summary>後台總覽儀表板與瀏覽趨勢。</summary>
public sealed record DashboardSummary(
    ContentCounts Posts,
    ContentCounts Pages,
    CommentService.CommentCounts Comments,
    int MediaCount,
    int RedirectCount,
    int TotalViews7Days,
    int TotalViews30Days,
    IReadOnlyList<DailyViewPoint> DailyViews,
    IReadOnlyList<TopContentItem> TopContents,
    IReadOnlyList<ContentSummary> RecentDrafts,
    IReadOnlyList<CommentService.CommentView> RecentComments);
public sealed record DailyViewPoint(DateOnly Date, int Views);
public sealed record TopContentItem(Guid Id, ContentKind Kind, string Title, string Slug, int ViewsPeriod, int ViewsTotal);
/// <summary>公開時間軸上相鄰的文章；比目前這篇新或舊。</summary>
public sealed record ContentNeighbor(Guid Id, string Slug, string Title, bool IsNewer);
/// <summary>公開彙整的單一月份與已發布文章數。</summary>
public sealed record ArchiveMonth(int Year, int Month, int Count);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)Total / PageSize));
}

public static partial class ContentRules
{
    public static string NormalizeSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new CmsException("網址代稱不得空白。");
        var result = SlugSeparators().Replace(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim(), "-").Trim('-');
        if (result.Length is 0 or > Limits.Slug) throw new CmsException($"網址代稱須為 1–{Limits.Slug} 字。");
        return result;
    }

    /// <summary>逸出 LIKE 萬用字元，讓使用者文字在 ILike 模式中逐字比對。</summary>
    public static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>
    /// 儲存的媒體檔名必須是圖片目錄內的單純檔案：不得有路徑
    /// 分隔字元、磁碟機代號或路徑穿越，且僅限點陣圖副檔名。
    /// </summary>
    public static bool IsSafeMediaFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName.Length <= 80
        && fileName.IndexOfAny(['/', '\\', ':']) < 0
        && !fileName.Any(char.IsControl)
        && Path.GetExtension(fileName).ToLowerInvariant() is ".png" or ".jpg" or ".webp";

    /// <summary>非圖片媒體的儲存檔名檢查：同樣禁止路徑穿越，但允許文件／音訊／影片副檔名。</summary>
    public static bool IsSafeMediaFileNameAny(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName.Length <= 80
        && fileName.IndexOfAny(['/', '\\', ':']) < 0
        && !fileName.Any(char.IsControl)
        && Path.GetExtension(fileName).ToLowerInvariant() is ".png" or ".jpg" or ".webp" or ".pdf" or ".mp3" or ".m4a"
            or ".mp4" or ".webm" or ".ogg" or ".wav";

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlugSeparators();

    public static void Validate(object model)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(model, new ValidationContext(model), errors, true))
            throw new CmsException("輸入不完整或超過長度上限，請檢查必填欄位。");
    }

    public static int Offset(int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > Limits.MaxPageSize || page > int.MaxValue / pageSize)
            throw new CmsException("分頁參數超出範圍。");
        return (page - 1) * pageSize;
    }

    public static async Task SaveAsync(DbContext db, CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new CmsException("資料已被另一個分頁更新或刪除，請重新載入後再編輯。"); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            throw new CmsException(postgres.ConstraintName switch
            {
                // 關聯表主鍵衝突代表兩個重疊的儲存都通過了版本檢查。
                "PK_ContentCategories" or "PK_ContentTags" => "資料已被另一個分頁更新，請重新載入後再編輯。",
                "IX_Contents_Slug" or "IX_Categories_Slug" or "IX_Tags_Slug" or
                "IX_Contents_SiteId_Slug" or "IX_Categories_SiteId_Slug" or "IX_Tags_SiteId_Slug"
                    => "網址代稱已存在，請使用其他代稱。",
                _ => "資料已存在或已被另一個分頁變更，請重新載入後再試。"
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { throw new CmsException("所選分類、標籤或封面圖已被變更，請重新載入。"); }
    }
}
