using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WordPressNet.Web.Models;

namespace WordPressNet.Web.Services;

public sealed class CmsException(string message) : Exception(message);

public sealed class ContentEdit
{
    public Guid? Id { get; set; }
    public ContentKind Kind { get; set; }
    [Required, StringLength(Limits.Title)] public string Title { get; set; } = "";
    [StringLength(Limits.Slug)] public string Slug { get; set; } = "";
    [StringLength(Limits.Body)] public string Markdown { get; set; } = "";
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
    public Guid Version { get; set; }
}

public sealed record ContentSummary(Guid Id, ContentKind Kind, string Title, string Slug,
    ContentStatus Status, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, Guid Version);
public sealed record TaxonomyItem(Guid Id, string Name, string Slug, Guid Version);
public sealed record PublicContent(Guid Id, ContentKind Kind, string Title, string Slug, string Markdown,
    DateTimeOffset? PublishedAt, List<string> Categories, List<string> Tags);
public sealed record NavigationItem(string Title, string Slug);
public sealed record MediaItem(Guid Id, string FileName, string OriginalName, string ContentType,
    long Size, int Width, int Height, DateTimeOffset UploadedAt)
{
    public string Url => "/media/" + FileName;
    public string Markdown => "![圖片](" + Url + ")";
}
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)Total / PageSize));
}

public static partial class ContentRules
{
    public static string NormalizeSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new CmsException("網址代稱不得空白。");
        var result = SlugSeparators().Replace(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim(), "-").Trim('-');
        if (result.Length is 0 or > Limits.Slug) throw new CmsException($"網址代稱須為 1–{Limits.Slug} 字。");
        return result;
    }

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
