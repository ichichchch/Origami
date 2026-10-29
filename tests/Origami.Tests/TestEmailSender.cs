namespace Origami.Tests;

/// <summary>測試用寄件服務：記錄寄出的郵件，供密碼重設等流程驗證。</summary>
internal sealed class TestEmailSender : IEmailSender
{
    public sealed record Sent(string To, string Subject, string Body);

    private readonly List<Sent> sent = [];
    public IReadOnlyList<Sent> Messages => sent;
    public bool IsConfigured { get; set; } = true;

    public Task<bool> SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        sent.Add(new Sent(to, subject, body));
        return Task.FromResult(IsConfigured);
    }
}
