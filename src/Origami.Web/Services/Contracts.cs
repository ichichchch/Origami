namespace Origami.Web.Services;

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
    /// <summary>Requires a password before the public page renders the body.</summary>
    public bool IsProtected { get; set; }
    /// <summary>Restricts the public URL to the signed-in administrator; everyone else gets 404.</summary>
    public bool IsPrivate { get; set; }
    /// <summary>Parent page for navigation hierarchy; pages only, posts always keep null.</summary>
    public Guid? ParentId { get; set; }
    /// <summary>Set a new protection password; empty keeps the stored one. Null when unchanged.</summary>
    public string? NewProtectPassword { get; set; }
    /// <summary>Read-only echo: a protection password is already stored.</summary>
    public bool HasProtectPassword { get; set; }
    /// <summary>Read-only echo of the stored view total.</summary>
    public int Views { get; set; }
    /// <summary>Future publish time; only meaningful while the content stays a draft.</summary>
    public DateTimeOffset? PublishAt { get; set; }
    public Guid? PreviewToken { get; set; }
    /// <summary>Read-only echo of the stored first-publish time.</summary>
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
    // A manual excerpt is rendered as authored; the fallback prefix still needs Markdown stripping.
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
    // The default tab lists drafts and published content; the trash has its own tab.
    public int Total => Drafts + Published;
}
/// <summary>Revision list entry; the body loads on demand.</summary>
public sealed record RevisionItem(Guid Id, string Title, DateTimeOffset CreatedAt);
public sealed record RevisionDetail(Guid Id, Guid ContentId, string Title, string Markdown, string Excerpt, DateTimeOffset CreatedAt);
public sealed record TaxonomyItem(Guid Id, string Name, string Slug, Guid Version)
{
    public string Description { get; init; } = "";
}
/// <summary>A public taxonomy with its authored description for filtered listings.</summary>
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
/// <summary>A page candidate for selectors in the editor and site settings.</summary>
public sealed record PageOption(Guid Id, string Title, Guid? ParentId, ContentStatus Status,
    bool IsPrivate = false, bool IsProtected = false);
/// <summary>One public URL for the sitemap with its last significant change.</summary>
public sealed record SitemapItem(string Path, DateTimeOffset? LastModified);
public sealed record MediaItem(Guid Id, string FileName, string OriginalName, string ContentType,
    long Size, int Width, int Height, DateTimeOffset UploadedAt)
{
    public string AltText { get; init; } = "";
    /// <summary>Set by listings; how many contents embed this image in any state.</summary>
    public int ReferenceCount { get; set; }
    public string Url => "/media/" + FileName;
    // An unset alt keeps the historical placeholder; brackets are escaped so authored text cannot break the syntax.
    public string Markdown => "![" + (AltText == "" ? "圖片" : AltText.Replace("[", "\\[").Replace("]", "\\]")) + "](" + Url + ")";
}
/// <summary>The live public target behind a retired slug, for permanent redirects.</summary>
public sealed record SlugRedirectTarget(string Slug, ContentKind Kind);
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

    /// <summary>Escapes LIKE wildcards so user text is matched literally in ILike patterns.</summary>
    public static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

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
                // Join-table keys mean two overlapping saves both passed the version check.
                "PK_ContentCategories" or "PK_ContentTags" => "資料已被另一個分頁更新，請重新載入後再編輯。",
                "IX_Contents_Slug" or "IX_Categories_Slug" or "IX_Tags_Slug" => "網址代稱已存在，請使用其他代稱。",
                _ => "資料已存在或已被另一個分頁變更，請重新載入後再試。"
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { throw new CmsException("所選分類或標籤已被變更，請重新載入。"); }
    }
}
