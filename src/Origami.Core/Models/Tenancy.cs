namespace Origami.Core.Models;

public static class SiteDefaults
{
    public static readonly Guid DefaultSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
}

public sealed class CmsSite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(Limits.SiteName)] public string Name { get; set; } = "";
    /// <summary>建立後不可變更的可讀站點代稱；持久化目錄以 Id 作為穩定識別。</summary>
    [Required, StringLength(Limits.SiteSlug)] public string Slug { get; set; } = "";
    /// <summary>正規化為小寫 ASCII 的完整主機名稱，不含通訊埠；僅預設站點可為 null。</summary>
    [StringLength(Limits.HostName)] public string? HostName { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
}

// 順序即權限高低：Author 可撰寫與提交自己的內容，Editor 以上才能發布。
// 2026-10-28 重編號（原 Editor=1/Administrator=2），既有資料由遷移回填 +1。
public enum SiteRole { Reader = 0, Author = 1, Editor = 2, Administrator = 3 }

public sealed class SiteMembership
{
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public string UserId { get; set; } = "";
    public SiteRole Role { get; set; }
    /// <summary>此站點內的公開顯示名稱；作為該成員建立內容時的作者署名，空字串退回站點設定。</summary>
    [StringLength(Limits.AuthorName)] public string DisplayName { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public CmsSite Site { get; set; } = null!;
    public AdminUser User { get; set; } = null!;
}

public enum ApiCredentialScope { Read = 0, Write = 1 }

/// <summary>受控導覽項目：指向站內頁面或外部連結，可排序與隱藏；未啟用時沿用頁面自動排序。</summary>
public sealed class SiteNavItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    /// <summary>導覽顯示文字。</summary>
    [Required, StringLength(Limits.NavTitle)] public string Title { get; set; } = "";
    /// <summary>指向的站內頁面；外部連結時為 null。</summary>
    public Guid? ContentId { get; set; }
    public Content? Content { get; set; }
    /// <summary>外部連結的絕對網址（僅 http／https）；站內頁面項目為空字串。</summary>
    [StringLength(Limits.NavUrl)] public string Url { get; set; } = "";
    /// <summary>顯示順序；數字越小越靠前。</summary>
    public int Position { get; set; }
    /// <summary>是否顯示於前台導覽。</summary>
    public bool IsVisible { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
    public CmsSite Site { get; set; } = null!;
}
public sealed class ApiCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public string UserId { get; set; } = "";
    [Required, StringLength(Limits.CredentialName)] public string Name { get; set; } = "";
    /// <summary>僅儲存秘密的 SHA-256 十六進位雜湊；原始秘密不得落庫。</summary>
    [Required, StringLength(64)] public string SecretHash { get; set; } = "";
    [Required, StringLength(256)] public string SecurityStamp { get; set; } = "";
    public ApiCredentialScope Scope { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public CmsSite Site { get; set; } = null!;
    public AdminUser User { get; set; } = null!;
}
