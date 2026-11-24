namespace Origami.Tests;

/// <summary>帳號與信件：信箱驗證、變更信箱確認、密碼變更通知、寄信紀錄、刪除帳號轉移內容與登入歷史。</summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountMailTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static string Origin => "https://cms.example.test";

    private static string TokenOf(string body)
    {
        var match = System.Text.RegularExpressions.Regex.Match(body, "token=([^&\\s]+)");
        Assert.True(match.Success, body);
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    private async Task<(UserSummary User, TestEmailSender Mail)> CreateUserWithMailAsync(string name, string email)
    {
        using var admin = await App.AdministratorAsync();
        var mail = admin.Services.GetRequiredService<TestEmailSender>();
        var user = await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = name, Password = IsolatedPostgres.NewPassword(), Email = email });
        return (user, mail);
    }

    [PostgresFact]
    public async Task Email_verification_link_confirms_the_mailbox()
    {
        var (user, mail) = await CreateUserWithMailAsync("verify-user", "verify@example.test");
        using var actor = await App.UserAsync(user.Id);
        var emails = actor.Services.GetRequiredService<AccountEmailService>();
        mail.Reset();

        Assert.True(await emails.SendVerificationAsync(user.Id, Origin));
        var sent = Assert.Single(mail.Messages);
        Assert.Equal("verify@example.test", sent.To);
        Assert.Contains(Origin + "/account/verify-email?user=", sent.Body, StringComparison.Ordinal);

        // 無效權杖被拒絕；有效權杖完成驗證；重複驗證維持成功。
        Assert.False((await emails.ConfirmEmailAsync(user.Id, "bad-token")).Succeeded);
        var other = await CreateUserWithMailAsync("verify-other", "other@example.test");
        Assert.False((await emails.ConfirmEmailAsync(other.User.Id, TokenOf(sent.Body))).Succeeded);
        var outcome = await emails.ConfirmEmailAsync(user.Id, TokenOf(sent.Body));
        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.True((await emails.ConfirmEmailAsync(user.Id, TokenOf(sent.Body))).Succeeded);
        // 已驗證後不再寄驗證信。
        Assert.False(await emails.SendVerificationAsync(user.Id, Origin));
        Assert.Single(mail.Messages);
    }

    [PostgresFact]
    public async Task Email_verification_token_is_invalidated_by_a_security_stamp_change()
    {
        var (user, mail) = await CreateUserWithMailAsync("verify-stamp", "stamp@example.test");
        using var actor = await App.UserAsync(user.Id);
        var emails = actor.Services.GetRequiredService<AccountEmailService>();
        mail.Reset();
        Assert.True(await emails.SendVerificationAsync(user.Id, Origin));
        var token = TokenOf(Assert.Single(mail.Messages).Body);
        var account = (await actor.Users.FindByIdAsync(user.Id))!;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        var updated = await actor.Users.UpdateAsync(account);
        Assert.True(updated.Succeeded);
        Assert.False((await emails.ConfirmEmailAsync(user.Id, token)).Succeeded);
        Assert.False((await actor.Users.FindByIdAsync(user.Id))!.EmailConfirmed);
    }

    [PostgresFact]
    public async Task Email_change_requires_confirmation_at_the_new_address()
    {
        var (user, mail) = await CreateUserWithMailAsync("change-user", "old@example.test");
        using (var actor = await App.UserAsync(user.Id))
        {
            var emails = actor.Services.GetRequiredService<AccountEmailService>();
            mail.Reset();

            // 個人資料儲存不直接改信箱：原信箱維持不變，回報待確認的新信箱。
            var profile = await actor.Sites.UpdateMyProfileAsync(new AccountProfileEdit
            {
                DisplayName = "小美", Email = "new@example.test", Bio = "簡介", WebsiteUrl = "", AvatarUrl = ""
            });
            Assert.Equal("old@example.test", profile.Email);
            Assert.Equal("new@example.test", profile.PendingEmailChange);

            // 確認信寄到「新」信箱，信內含限時權杖。
            Assert.True(await emails.RequestEmailChangeAsync(user.Id, "new@example.test", Origin));
            var sent = Assert.Single(mail.Messages);
            Assert.Equal("new@example.test", sent.To);
            Assert.Contains("/account/confirm-email-change?user=", sent.Body, StringComparison.Ordinal);
            // 確認前原信箱不變。
            Assert.Equal("old@example.test", (await actor.Users.FindByIdAsync(user.Id))!.Email);

            var token = TokenOf(sent.Body);
            Assert.False((await emails.ConfirmEmailChangeAsync(user.Id, "wrong@example.test", token)).Succeeded);
            var other = await CreateUserWithMailAsync("change-target-other", "other-change@example.test");
            Assert.False((await emails.ConfirmEmailChangeAsync(other.User.Id, "new@example.test", token)).Succeeded);
            var outcome = await emails.ConfirmEmailChangeAsync(user.Id, "new@example.test", token);
            Assert.True(outcome.Succeeded, outcome.Message);
            var changed = (await actor.Users.FindByIdAsync(user.Id))!;
            Assert.Equal("new@example.test", changed.Email);
            Assert.True(changed.EmailConfirmed);

            // 已被其他帳號使用的信箱不可申請；與目前相同者不寄信。
            await CreateUserWithMailAsync("change-other", "taken@example.test");
            await Assert.ThrowsAsync<CmsException>(() => emails.RequestEmailChangeAsync(user.Id, "taken@example.test", Origin));
            Assert.False(await emails.RequestEmailChangeAsync(user.Id, "new@example.test", Origin));
        }
    }

    [PostgresFact]
    public async Task Email_change_rejects_an_address_owned_by_another_account()
    {
        var (user, mail) = await CreateUserWithMailAsync("change-user-b", "old-b@example.test");
        using var other = await App.AdministratorAsync();
        await other.Sites.CreateUserAsync(new UserCreateInput { UserName = "taken-b", Password = IsolatedPostgres.NewPassword(), Email = "taken-b@example.test" });
        using var actor = await App.UserAsync(user.Id);
        var emails = actor.Services.GetRequiredService<AccountEmailService>();
        mail.Reset();
        await Assert.ThrowsAsync<CmsException>(() => emails.RequestEmailChangeAsync(user.Id, "taken-b@example.test", Origin));
        Assert.Empty(mail.Messages);
    }

    [PostgresFact]
    public async Task Password_change_notification_and_test_email_are_sent_and_logged()
    {
        var (user, mail) = await CreateUserWithMailAsync("notify-user", "notify@example.test");
        using var actor = await App.UserAsync(user.Id);
        var emails = actor.Services.GetRequiredService<AccountEmailService>();
        mail.Reset();

        // 以重設權杖設定已知密碼，再走自助修改密碼流程；成功後寄發通知。
        var account = (await actor.Users.FindByIdAsync(user.Id))!;
        var known = IsolatedPostgres.NewPassword();
        await actor.Users.ResetPasswordAsync(account, await actor.Users.GeneratePasswordResetTokenAsync(account), known);
        using var fresh = await App.UserAsync(user.Id);
        mail.Reset();
        await fresh.Sites.ChangeMyPasswordAsync(new ChangePasswordInput { CurrentPassword = known, NewPassword = IsolatedPostgres.NewPassword() });
        Assert.Contains(mail.Messages, m => m.To == "notify@example.test" && m.Subject.Contains("密碼已變更", StringComparison.Ordinal));

        // 測試郵件：格式錯誤或郵件服務未設定時回失敗；其餘照常寄出。
        Assert.False((await emails.SendTestAsync("not-an-email")).Succeeded);
        mail.IsConfigured = false;
        Assert.False((await emails.SendTestAsync("admin@example.test")).Succeeded);
        mail.IsConfigured = true;
        Assert.True((await emails.SendTestAsync("admin@example.test")).Succeeded);

        // 寄信紀錄包裝：成功與失敗各記一筆摘要，不含內文。
        var logging = fresh.Services.GetRequiredService<IEmailSender>();
        Assert.True(await logging.SendAsync("a@example.test", "第一封", "秘密內容"));
        mail.IsConfigured = false;
        Assert.False(await logging.SendAsync("b@example.test", "第二封", "秘密內容"));
        var log = await emails.ListEmailLogAsync();
        var entries = log.Items.Take(2).ToList();
        Assert.Equal("第二封", entries[0].Subject);
        Assert.False(entries[0].Succeeded);
        Assert.Equal("SMTP 未設定", entries[0].Error);
        Assert.Equal("第一封", entries[1].Subject);
        Assert.True(entries[1].Succeeded);
        Assert.DoesNotContain(log.Items, x => x.Error.Contains("秘密內容", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task Email_log_is_scoped_to_the_site_that_sent_the_message()
    {
        var otherSite = await CreateSiteAsync();
        using (var admin = await App.AdministratorAsync())
        {
            var mail = admin.Services.GetRequiredService<IEmailSender>();
            Assert.True(await mail.SendAsync("first@example.test", "第一站信件", "內容"));
        }
        using (var otherAdmin = await App.AdministratorAsync(otherSite.Id))
        {
            var mail = otherAdmin.Services.GetRequiredService<IEmailSender>();
            Assert.True(await mail.SendAsync("second@example.test", "第二站信件", "內容"));
            var emails = otherAdmin.Services.GetRequiredService<AccountEmailService>();
            var log = await emails.ListEmailLogAsync();
            var entry = Assert.Single(log.Items);
            Assert.Equal("第二站信件", entry.Subject);
            Assert.Equal(otherSite.Id, entry.SiteId);
        }
        using (var admin = await App.AdministratorAsync())
        {
            var log = await admin.Services.GetRequiredService<AccountEmailService>().ListEmailLogAsync();
            var entry = Assert.Single(log.Items);
            Assert.Equal("第一站信件", entry.Subject);
            Assert.Equal(SiteDefaults.DefaultSiteId, entry.SiteId);
        }
    }

    [PostgresFact]
    public async Task Deleting_a_user_transfers_or_clears_bylines_and_records_audit()
    {
        using var admin = await App.AdministratorAsync();
        var recipient = await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = "recipient", Password = IsolatedPostgres.NewPassword() });
        await admin.Sites.SetMemberAsync(new MemberEditInput { UserName = "recipient", Role = SiteRole.Editor, DisplayName = "接手編輯" });
        var author = await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = "leaving-author", Password = IsolatedPostgres.NewPassword(), Email = "leaving@example.test" });
        await admin.Sites.SetMemberAsync(new MemberEditInput { UserName = "leaving-author", Role = SiteRole.Author, DisplayName = "離職作者" });

        var content = await admin.Content.SaveAsync(NewContent("交接文章", status: ContentStatus.Published));
        var kept = await admin.Content.SaveAsync(NewContent("不轉移文章", status: ContentStatus.Published));
        foreach (var id in new[] { content.Id!.Value, kept.Id!.Value })
        {
            await admin.Content.BulkAsync(BulkAction.SetAuthor, [(id, (await admin.Content.GetEditAsync(id)).Version)],
                options: new BulkEditOptions(AuthorId: author.Id));
        }
        Assert.Equal("離職作者", (await admin.Content.GetPublishedAsync(ContentKind.Post, content.Slug ?? ""))!.AuthorName);

        // 不可刪除自己（操作者即全站管理者）；接收者不可是要刪除的帳號本身。
        var networkAdmin = (await admin.Users.FindByNameAsync("test-admin"))!;
        await Assert.ThrowsAsync<CmsException>(() => admin.Sites.DeleteUserAsync(networkAdmin.Id, null));
        await Assert.ThrowsAsync<CmsException>(() => admin.Sites.DeleteUserAsync(author.Id, author.Id));

        // 交接文章轉移給 recipient：署名沿用其在本站的顯示名稱。
        await admin.Sites.DeleteUserAsync(author.Id, recipient.Id);
        var transferred = (await admin.Content.GetPublishedAsync(ContentKind.Post, content.Slug ?? ""))!;
        Assert.Equal(recipient.Id, (await admin.Content.GetEditAsync(content.Id.Value))!.AuthorId);
        Assert.Equal("接手編輯", transferred.AuthorName);
        Assert.Null((await admin.Users.FindByNameAsync("leaving-author")));
        Assert.DoesNotContain(await admin.Sites.ListMembersAsync(), m => m.UserId == author.Id);

        // 不指定接收者：內容保留、作者欄清空。
        var orphan = await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = "orphan-author", Password = IsolatedPostgres.NewPassword() });
        await admin.Sites.SetMemberAsync(new MemberEditInput { UserName = "orphan-author", Role = SiteRole.Author, DisplayName = "匿名作者" });
        await admin.Content.BulkAsync(BulkAction.SetAuthor, [(kept.Id!.Value, (await admin.Content.GetEditAsync(kept.Id.Value)).Version)],
            options: new BulkEditOptions(AuthorId: orphan.Id));
        await admin.Sites.DeleteUserAsync(orphan.Id, null);
        var anonymized = (await admin.Content.GetPublishedAsync(ContentKind.Post, kept.Slug ?? ""))!;
        Assert.Null((await admin.Content.GetEditAsync(kept.Id.Value))!.AuthorId);
        Assert.Equal("", anonymized.AuthorName);
    }

    [PostgresFact]
    public async Task Deleting_last_site_administrator_is_rejected_and_external_logins_are_removed()
    {
        using var admin = await App.AdministratorAsync();
        var site = await admin.Sites.CreateSiteAsync(new SiteCreateInput { Name = "交接站", Slug = "handover", HostName = "handover.example.test" });
        var last = await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = "only-admin", Password = IsolatedPostgres.NewPassword() });
        using (var siteAdmin = await App.AdministratorAsync(site.Id))
            await siteAdmin.Sites.SetMemberAsync(new MemberEditInput { UserName = "only-admin", Role = SiteRole.Administrator });
        using (var db = await App.CreateContextAsync())
        {
            db.UserLogins.Add(new IdentityUserLogin<string>
            {
                UserId = last.Id, LoginProvider = "Test", ProviderKey = "only-admin", ProviderDisplayName = "Test"
            });
            await db.SaveChangesAsync();
        }
        using (var siteAdmin = await App.AdministratorAsync(site.Id))
        {
            var bootstrapId = await App.AdminUserIdAsync();
            var bootstrap = (await siteAdmin.Sites.ListMembersAsync()).Single(x => x.UserId == bootstrapId);
            await siteAdmin.Sites.RemoveMemberAsync(bootstrapId, bootstrap.Version);
            await Assert.ThrowsAsync<CmsException>(() => siteAdmin.Sites.DeleteUserAsync(last.Id, null));
        }
        Assert.NotNull(await admin.Users.FindByIdAsync(last.Id));
        using (var db = await App.CreateContextAsync()) Assert.Single(await db.UserLogins.ToListAsync());

        using (var siteAdmin = await App.AdministratorAsync(site.Id))
        {
            var backup = await siteAdmin.Sites.CreateUserAsync(new UserCreateInput { UserName = "backup-admin", Password = IsolatedPostgres.NewPassword() });
            await siteAdmin.Sites.SetMemberAsync(new MemberEditInput { UserName = "backup-admin", Role = SiteRole.Administrator });
            await siteAdmin.Sites.DeleteUserAsync(last.Id, null);
        }
        using (var db = await App.CreateContextAsync()) Assert.Empty(await db.UserLogins.ToListAsync());
    }

    [PostgresFact]
    public async Task Login_history_keeps_the_latest_events_per_account()
    {
        var (user, _) = await CreateUserWithMailAsync("history-user", "");
        using var admin = await App.AdministratorAsync();
        var at = DateTimeOffset.UtcNow;
        await AccountEmailService.RecordLoginAsync(App.ContextFactory, user.Id, user.UserName, false, "10.0.0.9", "curl/8", at.AddMinutes(-2));
        await AccountEmailService.RecordLoginAsync(App.ContextFactory, user.Id, user.UserName, true, "10.0.0.8", "Edge", at);
        using var actor = await App.UserAsync(user.Id);
        var history = await actor.Services.GetRequiredService<AccountEmailService>().ListLoginHistoryAsync(user.Id);
        Assert.Equal(2, history.Count);
        Assert.True(history[0].Succeeded);
        Assert.Equal("10.0.0.8", history[0].RemoteIp);
        Assert.False(history[1].Succeeded);
    }
}
