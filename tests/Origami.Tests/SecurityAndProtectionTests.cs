namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SecurityAndProtectionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    /// <summary>固定時鐘讓 RFC 6238 測試向量與服務流程都能具確定性。</summary>
    private sealed class FrozenTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string Rfc6238Code(byte[] key, long unixSeconds) // 測試專用的獨立迷你實作
    {
        var step = unixSeconds / 30;
        Span<byte> message = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { message[i] = (byte)(step & 0xff); step >>= 8; }
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(message.ToArray());
        var offset = hash[^1] & 0x0f;
        return (((hash[offset] << 24 | hash[offset + 1] << 16 | hash[offset + 2] << 8 | hash[offset + 3]) & 0x7fffffff) % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string value)
    {
        var bits = 0; var buffer = 0; var bytes = new List<byte>();
        foreach (var c in value)
        {
            var index = c is >= 'A' and <= 'Z' ? c - 'A' : c is >= 'a' and <= 'z' ? c - 'a' : c is >= '2' and <= '7' ? c - '2' + 26 : -1;
            if (index < 0) continue;
            buffer = (buffer << 5) | index; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xff)); bits -= 8; }
        }
        return [.. bytes];
    }

    [Fact]
    public void Totp_matches_the_rfc6238_vectors_and_rejects_garbage()
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"; // ASCII "12345678901234567890"
        var key = Base32Decode(secret);
        // RFC 6238 appendix B、SHA-1：t=59s → 287082；時鐘漂移視窗也接受 t=29s 與 t=89s。
        var clock = new FrozenTime(DateTimeOffset.UnixEpoch.AddSeconds(59));
        Assert.Equal("287082", Rfc6238Code(key, 59));
        Assert.True(Totp.Verify(secret, "287082", clock));
        Assert.True(Totp.Verify(secret, Rfc6238Code(key, 29), clock), "previous step accepted");
        Assert.True(Totp.Verify(secret, Rfc6238Code(key, 89), clock), "next step accepted");
        Assert.False(Totp.Verify(secret, Rfc6238Code(key, 119), clock), "two steps away rejected");
        Assert.False(Totp.Verify(secret, "abc12", clock));
        Assert.False(Totp.Verify(secret, null, clock));
        var fresh = Totp.GenerateSecret();
        Assert.Equal(32, fresh.Length);
        Assert.True(Totp.Verify(fresh, Rfc6238Code(Base32Decode(fresh), 59), clock));
        Assert.StartsWith("otpauth://totp/", Totp.OtpAuthUri(fresh, "admin", "Origami"), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Two_factor_flow_enables_disables_and_admin_reset_recovers()
    {
        var clock = new FrozenTime(DateTimeOffset.UtcNow);
        // 先重設密碼：輪替安全戳記會使已發出的測試主體失效。
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        // 啟用、停用與重設都會輪替安全戳記；每一步都要重新取得主體。
        async Task<(TestActor Actor, TwoFactorService Service)> FreshAsync()
        {
            var actor = await App.AdministratorAsync();
            var service = new TwoFactorService(actor.Services.GetRequiredService<AdminAccess>(),
                actor.Services.GetRequiredService<UserManager<AdminUser>>(),
                actor.Services.GetRequiredService<SignInManager<AdminUser>>(), clock);
            return (actor, service);
        }
        var (admin, twoFactor) = await FreshAsync();
        var initial = await twoFactor.GetStatusAsync();
        Assert.False(initial.Enabled);
        Assert.False(initial.SetupPending);

        var (secret, uri) = await twoFactor.BeginSetupAsync();
        Assert.Contains("secret=" + secret, uri, StringComparison.Ordinal);
        var status = await twoFactor.GetStatusAsync();
        Assert.False(status.Enabled);
        Assert.True(status.SetupPending);

        await Assert.ThrowsAsync<CmsException>(() => twoFactor.EnableAsync("000000"));
        await twoFactor.EnableAsync(Rfc6238Code(Base32Decode(secret), clock.GetUtcNow().ToUnixTimeSeconds()));
        admin.Dispose();
        (admin, twoFactor) = await FreshAsync();
        Assert.True((await twoFactor.GetStatusAsync()).Enabled);

        await Assert.ThrowsAsync<CmsException>(() => twoFactor.DisableAsync("wrong-password", Rfc6238Code(Base32Decode(secret), clock.GetUtcNow().ToUnixTimeSeconds())));
        await twoFactor.DisableAsync(password, Rfc6238Code(Base32Decode(secret), clock.GetUtcNow().ToUnixTimeSeconds()));
        admin.Dispose();
        (admin, twoFactor) = await FreshAsync();
        Assert.False((await twoFactor.GetStatusAsync()).Enabled);

        // 遺失驗證器時由伺服器端救援：管理員重設也會一併停用兩步驗證。
        var (rotatedSecret, _) = await twoFactor.BeginSetupAsync();
        await twoFactor.EnableAsync(Rfc6238Code(Base32Decode(rotatedSecret), clock.GetUtcNow().ToUnixTimeSeconds()));
        await App.Provisioner.ResetPasswordAsync(IsolatedPostgres.NewPassword());
        admin.Dispose();
        var (recovered, recoveredService) = await FreshAsync();
        using (recovered)
        {
            Assert.False((await recoveredService.GetStatusAsync()).Enabled);
        }
    }

    [PostgresFact]
    public async Task Password_protection_gates_the_public_page_and_disappears_from_discovery()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("機密發布", status: ContentStatus.Published);
        post.Slug = "secret-publish";
        post.Markdown = "這段文字絕不能出現在鎖定頁或搜尋。";
        post.IsProtected = true;
        post.NewProtectPassword = "content-pass-1";
        var saved = await admin.Content.SaveAsync(post);
        Assert.True(saved.HasProtectPassword);

        await App.Worker.ProcessPendingAsync();
        await using (var db = await App.CreateContextAsync())
        {
            Assert.False(await db.ContentEmbeddings.AnyAsync(x => x.ContentId == saved.Id), "protected content never enters the index");
        }

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var html = WebUtility.HtmlDecode(await (await client.GetAsync("/posts/secret-publish")).Content.ReadAsStringAsync());
        Assert.Contains("受密碼保護", html);
        Assert.Contains("noindex", html);
        Assert.DoesNotContain("這段文字絕不能出現在鎖定頁", html);
        var home = WebUtility.HtmlDecode(await (await client.GetAsync("/")).Content.ReadAsStringAsync());
        Assert.Contains("此內容受密碼保護。", home);
        Assert.DoesNotContain("這段文字絕不能出現在鎖定頁", home);
        var search = await admin.Services.GetRequiredService<PublicSearchService>().SearchAsync("這段文字絕不能");
        Assert.DoesNotContain(search.Hits, x => x.Slug == "secret-publish");

        // 服務層解鎖：正確密碼換得 token；變更密碼會使舊 token 失效。
        var token = await admin.Content.VerifyProtectionAsync(saved.Id!.Value, "content-pass-1");
        Assert.NotNull(token);
        Assert.Null(await admin.Content.VerifyProtectionAsync(saved.Id.Value, "wrong"));
        Assert.True(await admin.Content.IsUnlockedAsync(saved.Id.Value, token));
        var change = await admin.Content.GetEditAsync(saved.Id.Value);
        change.NewProtectPassword = "content-pass-2";
        await admin.Content.UpdateAsync(change);
        Assert.False(await admin.Content.IsUnlockedAsync(saved.Id.Value, token), "old cookies die with the old password");
        Assert.True(await admin.Content.IsUnlockedAsync(saved.Id.Value, await admin.Content.VerifyProtectionAsync(saved.Id.Value, "content-pass-2")));

        // MCP 絕不能成為窺探受保護本文的側門。
        var tools = admin.Services.GetRequiredService<McpReadTools>();
        var mcp = await tools.GetPostAsync("secret-publish");
        Assert.False(mcp.Found);

        // 首次啟用保護時未設密碼會被拒絕；密碼過短同樣被拒絕。
        var other = NewContent("另一篇", status: ContentStatus.Published);
        other.Slug = "protect-rules";
        other.IsProtected = true;
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(other));
        other.NewProtectPassword = "short";
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(other));
        other.NewProtectPassword = "long-enough-pass";
        var otherSaved = await admin.Content.SaveAsync(other);
        Assert.True(otherSaved.HasProtectPassword);
    }

    [PostgresFact]
    public async Task Private_content_is_visible_only_to_the_administrator()
    {
        // 重設會輪替安全戳記，先備妥已知密碼讓測試之後仍能經 HTTP 登入。
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var admin = await App.AdministratorAsync();
        var post = NewContent("僅管理員可見", status: ContentStatus.Published);
        post.Slug = "private-post";
        post.Markdown = "這段私密文字絕不能外洩。";
        post.IsPrivate = true;
        var saved = await admin.Content.SaveAsync(post);

        // 私密文字和受密碼保護文字一樣，絕不進入嵌入索引。
        await App.Worker.ProcessPendingAsync();
        await using (var db = await App.CreateContextAsync())
        {
            Assert.False(await db.ContentEmbeddings.AnyAsync(x => x.ContentId == saved.Id), "private content never enters the index");
        }

        // 私密與密碼保護互斥，不得同時設定。
        var conflict = NewContent("矛盾設定", status: ContentStatus.Published);
        conflict.IsProtected = true;
        conflict.NewProtectPassword = "long-enough-pass";
        conflict.IsPrivate = true;
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(conflict));

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        // 匿名訪客會得到與內容不存在相同的 404。
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/private-post")).StatusCode);
        var home = WebUtility.HtmlDecode(await (await client.GetAsync("/")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("private-post", home);
        Assert.DoesNotContain("僅管理員可見", home);
        Assert.DoesNotContain("private-post", await (await client.GetAsync("/sitemap.xml")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("private-post", await (await client.GetAsync("/feed")).Content.ReadAsStringAsync());
        var search = await admin.Services.GetRequiredService<PublicSearchService>().SearchAsync("私密文字");
        Assert.DoesNotContain(search.Hits, x => x.Slug == "private-post");
        var tools = admin.Services.GetRequiredService<McpReadTools>();
        Assert.False((await tools.GetPostAsync("private-post")).Found, "MCP must not reach private bodies");

        // 已登入的管理員能讀取同一網址，並看到顯示出來的私密標記。
        var cookies = new CookieContainer();
        using (var signedIn = site.CreateClient(cookies: cookies))
        {
            using var login = await HttpSecurityTests.LoginAsync(signedIn, password);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            var page = await signedIn.GetAsync("/posts/private-post");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            Assert.Contains("私密", html);
            Assert.Contains("這段私密文字絕不能外洩。", html);
            Assert.Contains("noindex", html);
        }

        // 切回公開後，網址、列表與嵌入管道都會恢復。
        var edit = await admin.Content.GetEditAsync(saved.Id!.Value);
        edit.IsPrivate = false;
        await admin.Content.UpdateAsync(edit);
        await App.Worker.ProcessPendingAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/posts/private-post")).StatusCode);
        var homeAgain = WebUtility.HtmlDecode(await (await client.GetAsync("/")).Content.ReadAsStringAsync());
        Assert.Contains("僅管理員可見", homeAgain);
        await using (var db = await App.CreateContextAsync())
        {
            Assert.True(await db.ContentEmbeddings.AnyAsync(x => x.ContentId == saved.Id), "re-embedding follows the private-to-public switch");
        }
    }

    [PostgresFact]
    public async Task Views_are_counted_per_day_and_surface_in_the_admin_list()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("有人看的文章", status: ContentStatus.Published);
        post.Slug = "viewed-post";
        var saved = await admin.Content.SaveAsync(post);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/posts/viewed-post")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/posts/viewed-post")).StatusCode);

        await using (var db = await App.CreateContextAsync())
        {
            var row = await db.ContentViews.SingleOrDefaultAsync(x => x.ContentId == saved.Id);
            Assert.NotNull(row);
            Assert.Equal(2, row!.Count);
        }
        var listed = (await admin.Content.ListAdminAsync(ContentKind.Post)).Items.Single(x => x.Slug == "viewed-post");
        Assert.Equal(2, listed.Views);
        var edit = await admin.Content.GetEditAsync(saved.Id!.Value);
        Assert.Equal(2, edit.Views);
    }
}
