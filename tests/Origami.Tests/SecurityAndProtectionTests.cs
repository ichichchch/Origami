namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SecurityAndProtectionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    /// <summary>A fixed clock lets the RFC 6238 vectors and service flows be deterministic.</summary>
    private sealed class FrozenTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string Rfc6238Code(byte[] key, long unixSeconds) // independent mini-implementation for the tests
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
        // RFC 6238 appendix B, SHA-1: t=59s → 287082; drift window also accepts t=29s and t=89s.
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
        // Reset first: rotating the stamp would invalidate an already-issued test principal.
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        // Enabling, disabling and resets all rotate the security stamp; each step needs a fresh principal.
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

        // A lost authenticator is recovered server-side: admin-reset also disables two-factor.
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

        // Service-level unlock: correct password yields a token; changing the password voids it.
        var token = await admin.Content.VerifyProtectionAsync(saved.Id!.Value, "content-pass-1");
        Assert.NotNull(token);
        Assert.Null(await admin.Content.VerifyProtectionAsync(saved.Id.Value, "wrong"));
        Assert.True(await admin.Content.IsUnlockedAsync(saved.Id.Value, token));
        var change = await admin.Content.GetEditAsync(saved.Id.Value);
        change.NewProtectPassword = "content-pass-2";
        await admin.Content.UpdateAsync(change);
        Assert.False(await admin.Content.IsUnlockedAsync(saved.Id.Value, token), "old cookies die with the old password");
        Assert.True(await admin.Content.IsUnlockedAsync(saved.Id.Value, await admin.Content.VerifyProtectionAsync(saved.Id.Value, "content-pass-2")));

        // MCP never becomes a side door into gated bodies.
        var tools = admin.Services.GetRequiredService<McpReadTools>();
        var mcp = await tools.GetPostAsync("secret-publish");
        Assert.False(mcp.Found);

        // First-time enable without a password is refused; a short password is refused.
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
