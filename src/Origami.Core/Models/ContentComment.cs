namespace Origami.Core.Models;

public enum CommentStatus { Pending = 0, Approved = 1, Spam = 2, Trashed = 3 }

/// <summary>訪客評論：張貼時預設待審，核可後才會公開顯示；渲染走與內容相同的 Markdown 安全管線。</summary>
public sealed class ContentComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public Guid ContentId { get; set; }
    public Content Content { get; set; } = null!;
    /// <summary>訪客顯示名稱；不關聯任何帳號。</summary>
    [Required, StringLength(Limits.CommentAuthor)] public string AuthorName { get; set; } = "";
    /// <summary>訪客網址（選填，僅 http／https）。</summary>
    [StringLength(Limits.CommentUrl)] public string Url { get; set; } = "";
    [Required, StringLength(Limits.CommentBody)] public string Markdown { get; set; } = "";
    /// <summary>父評論識別（樓中樓回覆）；最上層評論為 null。</summary>
    public Guid? ParentId { get; set; }
    public ContentComment? Parent { get; set; }
    /// <summary>是否為後台管理員／編輯者的官方回覆。</summary>
    public bool IsAdminReply { get; set; }
    public CommentStatus Status { get; set; } = CommentStatus.Pending;
    /// <summary>審核並發版本；兩個後台分頁同時處理同一則評論時，較舊的版本不得覆寫。</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public CmsSite Site { get; set; } = null!;
}

public static partial class Limits
{
    public const int CommentAuthor = 80;
    public const int CommentUrl = 500;
    public const int CommentBody = 4000;
    /// <summary>單篇內容單頁顯示的評論數上限。</summary>
    public const int CommentsPageSize = 50;
    /// <summary>評論內出現超過此數量的連結時直接標記為垃圾（基礎過濾）。</summary>
    public const int CommentMaxLinks = 3;
}
