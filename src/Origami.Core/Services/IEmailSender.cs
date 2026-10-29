namespace Origami.Core.Services;

/// <summary>郵件寄送抽象；主機端以實際 SMTP 實作，未設定時以 <see cref="IsConfigured"/> 明確回報無法寄送。</summary>
public interface IEmailSender
{
    /// <summary>是否已設定可用的寄件設定（主機、寄件者等）。</summary>
    bool IsConfigured { get; }
    /// <summary>寄出郵件；回傳是否成功寄出。</summary>
    Task<bool> SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

/// <summary>
/// 以 SMTP 寄送郵件。連線資訊與金鑰一律由外部設定提供（環境變數或使用者密鑰），
/// 不進版控；未設定時 <see cref="IsConfigured"/> 為 false，呼叫端據此顯示明確訊息。
/// </summary>
public sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly string host = configuration["Smtp:Host"] ?? "";
    private readonly int port = int.TryParse(configuration["Smtp:Port"], out var parsed) ? parsed : 587;
    private readonly string user = configuration["Smtp:User"] ?? "";
    private readonly string password = configuration["Smtp:Password"] ?? "";
    private readonly string from = configuration["Smtp:From"] ?? "";
    private readonly bool enableSsl = !string.Equals(configuration["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured => host.Length > 0 && from.Length > 0;

    public async Task<bool> SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("SMTP 未設定，略過寄信：{Subject}", subject);
            return false;
        }
        try
        {
            using var client = new System.Net.Mail.SmtpClient(host, port) { EnableSsl = enableSsl };
            if (user.Length > 0) client.Credentials = new System.Net.NetworkCredential(user, password);
            using var message = new System.Net.Mail.MailMessage(from, to, subject, body) { IsBodyHtml = false };
            await client.SendMailAsync(message, ct);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 寄信失敗不得洩漏連線資訊；只記錄摘要。
            logger.LogError(error, "郵件寄送失敗：{Subject}", subject);
            return false;
        }
    }
}
