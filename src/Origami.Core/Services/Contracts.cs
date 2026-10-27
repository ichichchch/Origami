namespace Origami.Core.Services;

public sealed class CmsException(string message) : Exception(message);

public sealed class ContentEdit
{
    public Guid? Id { get; set; }
    public ContentKind Kind { get; set; }
    [Required, StringLength(Limits.Title)] public string Title { get; set; } = "";
    [StringLength(Limits.Slug)] public string? Slug { get; set; }
    [StringLength(Limits.Body)] public string? Markdown { get; set; }
    [StringLength(Limits.Excerpt)] public string? Excerpt { get; set; }
    public bool IsSticky { get; set; }
    /// <summary>公開頁面渲染本文前須先輸入密碼。</summary>
    public bool IsProtected { get; set; }
    /// <summary>公開網址僅限已登入管理員；其他人一律得到 404。</summary>
    public bool IsPrivate { get; set; }
    /// <summary>導覽階層的上層頁面；僅頁面可用，文章一律保持 null。</summary>
    public Guid? ParentId { get; set; }
    /// <summary>設定新的保護密碼；空字串保留已儲存者。未變更時為 null。</summary>
    public string? NewProtectPassword { get; set; }
    /// <summary>唯讀回報：已儲存保護密碼。</summary>
    public bool HasProtectPassword { get; set; }
    /// <summary>已儲存瀏覽總數的唯讀回報。</summary>
    public int Views { get; set; }
    /// <summary>未來的發布時間；僅在內容維持草稿時有意義。</summary>
    public DateTimeOffset? PublishAt { get; set; }
    public Guid? PreviewToken { get; set; }
    /// <summary>已儲存首次發布時間的唯讀回報。</summary>
    public DateTimeOffset? PublishedAt { get; set; }
    public ContentStatus Status { get; set; }
    public Guid Version { get; set; }
    public List<Guid> CategoryIds { get; set; } = [];
    public List<Guid> TagIds { get; set; } = [];
}

public sealed class TaxonomyEdit
{
    public Guid? Id { get; set; }
    [Required, StringLength(Limits.TaxonomyName)] public string Name { get; set; } = "";
    [StringLength(Limits.Slug)] public string Slug { get; set; } = "";
    [StringLength(Limits.TaxonomyDescription)] public string? Description { get; set; }
    public Guid Version { get; set; }
}

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
    public Guid? ParentId { get; init; }
    public string ParentTitle { get; init; } = "";
    public int Views { get; init; }
}
public sealed record ContentCounts(int Drafts, int Published, int Trashed = 0)
{
    // 預設分頁列出草稿與已發布內容；回收桶另有專屬分頁。
    public int Total => Drafts + Published;
}
/// <summary>修訂版本列表項目；本文於需要時載入。</summary>
public sealed record RevisionItem(Guid Id, string Title, DateTimeOffset CreatedAt);
public sealed record RevisionDetail(Guid Id, Guid ContentId, string Title, string Markdown, string Excerpt, DateTimeOffset CreatedAt);
public sealed record TaxonomyItem(Guid Id, string Name, string Slug, Guid Version)
{
    public string Description { get; init; } = "";
}
/// <summary>帶作者描述的公開分類或標籤，供篩選列表使用。</summary>
public sealed record TaxonomyView(string Title, string Slug, string Description = "");
public sealed record PublicTaxonomy(string Name, string Slug, int Count);
public sealed record PublicContent(Guid Id, ContentKind Kind, string Title, string Slug, string Markdown,
    DateTimeOffset? PublishedAt, IReadOnlyList<NavigationItem> Categories, IReadOnlyList<NavigationItem> Tags)
{
    public string Excerpt { get; init; } = "";
    public bool IsProtected { get; init; }
    public bool IsPrivate { get; init; }
}
public sealed record NavigationItem(string Title, string Slug);
/// <summary>公開導覽中的已發布頁面，附階層指標。</summary>
public sealed record PageNavItem(Guid Id, string Title, string Slug, Guid? ParentId);
/// <summary>編輯器與網站設定中選擇器的頁面候選。</summary>
public sealed record PageOption(Guid Id, string Title, Guid? ParentId, ContentStatus Status,
    bool IsPrivate = false, bool IsProtected = false);
/// <summary>sitemap 的單一公開網址，附上次重大變更時間。</summary>
public sealed record SitemapItem(string Path, DateTimeOffset? LastModified);
public sealed record MediaItem(Guid Id, string FileName, string OriginalName, string ContentType,
    long Size, int Width, int Height, DateTimeOffset UploadedAt)
{
    public string AltText { get; init; } = "";
    /// <summary>由列表填入；有多少內容在任何狀態下內嵌此圖片。</summary>
    public int ReferenceCount { get; set; }
    public string Url => "/media/" + FileName;
    // 未設定替代文字時沿用歷史預留字；括號會被逸出，作者文字無法破壞語法。
    public string Markdown => "![" + (AltText == "" ? "圖片" : AltText.Replace("[", "\\[").Replace("]", "\\]")) + "](" + Url + ")";
}
/// <summary>退役 slug 背後現行的公開目標，供永久轉址使用。</summary>
public sealed record SlugRedirectTarget(string Slug, ContentKind Kind);
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
                "IX_Contents_Slug" or "IX_Categories_Slug" or "IX_Tags_Slug" => "網址代稱已存在，請使用其他代稱。",
                _ => "資料已存在或已被另一個分頁變更，請重新載入後再試。"
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { throw new CmsException("所選分類或標籤已被變更，請重新載入。"); }
    }
}
