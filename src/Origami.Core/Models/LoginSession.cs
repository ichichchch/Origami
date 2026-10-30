namespace Origami.Core.Models;

/// <summary>網站登入工作階段：每次登入一列，供裝置清單與單一會話撤銷；屬識別網域，不隨站點隔離。</summary>
public sealed class LoginSessionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>登入使用者識別（AspNetUsers.Id）。</summary>
    [Required, StringLength(450)] public string UserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>登入當下的 User-Agent 原值截斷；僅供裝置辨識顯示。</summary>
    [StringLength(256)] public string UserAgent { get; set; } = "";
    /// <summary>登入當下的來源 IP。</summary>
    [StringLength(64)] public string RemoteIp { get; set; } = "";
    /// <summary>登入方式：password 或外部登入供應商名稱。</summary>
    [StringLength(64)] public string Provider { get; set; } = "";
}
