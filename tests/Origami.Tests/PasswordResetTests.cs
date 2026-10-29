namespace Origami.Tests;

/// <summary>
/// 密碼重設：申請連結寄出郵件、權杖重設成功、錯誤權杖與不符規則被拒、
/// 未註冊的電子郵件不寄信也不洩漏存在性。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PasswordResetTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Password = "Old-Password-2026";

    private async Task<(string UserId, string Email)> CreateUserWithEmailAsync(string name)
    {
        using var admin = await App.AdministratorAsync();
        var user = await admin.Sites.CreateUserAsync(new UserCreateInput
        {
            UserName = name, Password = Password, Email = $"{name}@example.test"
        });
        return (user.Id, $"{name}@example.test");
    }

    [PostgresFact]
    public async Task Reset_request_sends_a_link_that_can_reset_the_password()
    {
        var (userId, email) = await CreateUserWithEmailAsync("resetuser");
        using var actor = await App.AdministratorAsync();
        var sender = actor.Services.GetRequiredService<TestEmailSender>();
        var service = actor.Services.GetRequiredService<PasswordResetService>();

        var requested = await service.RequestResetAsync(email, "https://example.test");
        Assert.True(requested);
        var message = Assert.Single(sender.Messages);
        Assert.Equal(email, message.To);
        Assert.Contains("/account/reset-password?user=", message.Body);
        Assert.Contains("token=", message.Body);

        // 由信中連結取出權杖，完成重設。
        var link = message.Body.Split('\n').First(line => line.Contains("/account/reset-password?"));
        var uri = new Uri(link.Trim());
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var token = query["token"]!;
        Assert.Equal(userId, query["user"]);

        var outcome = await service.ResetAsync(userId, token, email, "New-Password-2026");
        Assert.True(outcome.Succeeded);
        var account = await actor.Users.FindByIdAsync(userId);
        Assert.NotNull(account);
        Assert.True(await actor.Users.CheckPasswordAsync(account!, "New-Password-2026"));
        // 重設後舊密碼失效。
        Assert.False(await actor.Users.CheckPasswordAsync(account!, Password));
    }

    [PostgresFact]
    public async Task Invalid_tokens_and_mismatched_emails_are_refused()
    {
        var (userId, email) = await CreateUserWithEmailAsync("resetuser2");
        using var actor = await App.AdministratorAsync();
        var service = actor.Services.GetRequiredService<PasswordResetService>();
        await service.RequestResetAsync(email, "https://example.test");

        var wrongToken = await service.ResetAsync(userId, "not-a-real-token", email, "New-Password-2026");
        Assert.False(wrongToken.Succeeded);
        Assert.DoesNotContain("token", wrongToken.Message, StringComparison.OrdinalIgnoreCase);

        var wrongEmail = await service.ResetAsync(userId, "not-a-real-token", "other@example.test", "New-Password-2026");
        Assert.False(wrongEmail.Succeeded);
    }

    [PostgresFact]
    public async Task Unknown_addresses_send_nothing_and_still_report_success()
    {
        await CreateUserWithEmailAsync("resetuser3");
        using var actor = await App.AdministratorAsync();
        var sender = actor.Services.GetRequiredService<TestEmailSender>();
        var service = actor.Services.GetRequiredService<PasswordResetService>();

        var requested = await service.RequestResetAsync("nobody@example.test", "https://example.test");
        Assert.True(requested);
        Assert.Empty(sender.Messages);
    }

    [PostgresFact]
    public async Task Disabled_accounts_cannot_reset_password()
    {
        var (userId, email) = await CreateUserWithEmailAsync("resetuser4");
        using var admin = await App.AdministratorAsync();
        await admin.Sites.SetUserDisabledAsync(userId, true);
        var sender = admin.Services.GetRequiredService<TestEmailSender>();
        var service = admin.Services.GetRequiredService<PasswordResetService>();

        await service.RequestResetAsync(email, "https://example.test");
        Assert.Empty(sender.Messages);
    }
}
