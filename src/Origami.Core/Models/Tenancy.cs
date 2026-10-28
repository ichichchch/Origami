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

public enum SiteRole { Reader = 0, Editor = 1, Administrator = 2 }

public sealed class SiteMembership
{
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public string UserId { get; set; } = "";
    public SiteRole Role { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public CmsSite Site { get; set; } = null!;
    public AdminUser User { get; set; } = null!;
}

public enum ApiCredentialScope { Read = 0, Write = 1 }

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
