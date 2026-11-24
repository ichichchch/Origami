namespace Origami.Core.Models;

/// <summary>
/// 寄信紀錄：每一封對外郵件的收件人、主旨與結果；失敗原因只記摘要（不含連線資訊或內文）。
/// 供後台檢視「信寄了沒、為什麼沒寄出去」。依站點隔離，不跨站共用。
/// </summary>
public sealed class EmailLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    [StringLength(256)] public string Recipient { get; set; } = "";
    [StringLength(300)] public string Subject { get; set; } = "";
    public bool Succeeded { get; set; }
    /// <summary>失敗摘要（例如 SMTP 未設定）；成功時為空字串。</summary>
    [StringLength(300)] public string Error { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public CmsSite Site { get; set; } = null!;
}

/// <summary>
/// 登入事件：成功與失敗各一筆，供成員檢視自己帳號的登入歷史。
/// 只記來源位址與使用者代理的截斷摘要，不記密碼或任何機密。
/// </summary>
public sealed class LoginEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [StringLength(256)] public string UserId { get; set; } = "";
    [StringLength(256)] public string UserName { get; set; } = "";
    public bool Succeeded { get; set; }
    [StringLength(64)] public string RemoteIp { get; set; } = "";
    [StringLength(256)] public string UserAgent { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
