using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace WordPressNet.Web.Models;

public enum ContentKind { Post, Page }
public enum ContentStatus { Draft, Published }
public enum TaxonomyKind { Category, Tag }

public static class Limits
{
    public const int Title = 200;
    public const int Slug = 160;
    public const int Body = 200_000;
    public const int TaxonomyName = 80;
    public const int SiteName = 100;
    public const int Description = 500;
    public const int PageSize = 10;
    public const int MaxPageSize = 50;
    public const int MaxTaxonomiesPerKind = 30;
    public const long UploadBytes = 5 * 1024 * 1024;
    public const long ImagePixels = 20_000_000;
    public const int ImageDimension = 10_000;
}

public sealed class AdminUser : IdentityUser
{
    // A unique, checked constant prevents concurrent provisioning of a second administrator.
    public int SingletonKey { get; set; } = 1;
}

public sealed class Content
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ContentKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Markdown { get; set; } = "";
    public ContentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
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
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class SiteSettings
{
    public int Id { get; set; } = 1;
    [Required, StringLength(Limits.SiteName)] public string Name { get; set; } = "WordPress.NET";
    [StringLength(Limits.Description)] public string Description { get; set; } = "用文字，分享你的世界。";
    public Guid Version { get; set; }
}
