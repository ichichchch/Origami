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

/// <summary>
/// 寄信紀錄包裝：每次寄信後寫一筆摘要（收件人、主旨、結果），失敗只記原因種類，
/// 不記連線資訊與內文。以範圍生命週期保留寄信當下的站點識別。
/// </summary>
public sealed class LoggingEmailSender(
    IEmailSender inner,
    IDbContextFactory<CmsDbContext> factory,
    SiteContext site,
    TimeProvider? clock = null) : IEmailSender
{
    private const int KeepPerRun = 200;

    public bool IsConfigured => inner.IsConfigured;

    public async Task<bool> SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var succeeded = await inner.SendAsync(to, subject, body, ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.EmailLogs.Add(new EmailLog
            {
                SiteId = site.SiteId,
                Recipient = Truncate(to, 256),
                Subject = Truncate(subject, 300),
                Succeeded = succeeded,
                Error = succeeded ? "" : inner.IsConfigured ? "寄送失敗（連線或收件者被拒）" : "SMTP 未設定",
                CreatedAt = (clock ?? TimeProvider.System).GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
            // 每個站點只保留最新的紀錄；寄信量低，逐次清理成本低。
            var stale = await db.EmailLogs.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip(KeepPerRun).Select(x => x.Id).ToListAsync(ct);
            if (stale.Count > 0)
                await db.EmailLogs.Where(x => stale.Contains(x.Id)).ExecuteDeleteAsync(ct);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // 紀錄失敗不得影響寄信結果。
        }
        return succeeded;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
