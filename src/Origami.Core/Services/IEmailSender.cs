namespace Origami.Core.Services;

/// <summary>郵件寄送抽象；主機端以實際 SMTP 實作，未設定時以 <see cref="IsConfigured"/> 明確回報無法寄送。</summary>
public interface IEmailSender
{
    /// <summary>是否已設定可用的寄件設定（主機、寄件者等）。</summary>
    bool IsConfigured { get; }
    /// <summary>寄出郵件；回傳是否成功寄出。</summary>
    Task<bool> SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
