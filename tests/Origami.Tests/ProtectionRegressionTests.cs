namespace Origami.Tests;

/// <summary>
/// 受保護、私密與兩步驟驗證內容之可見性規則的回歸測試。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProtectionRegressionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task A_malformed_unlock_cookie_never_reveals_the_protected_excerpt()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("受保護摘要", status: ContentStatus.Published);
        post.Slug = "protected-excerpt";
        post.Excerpt = "SECRET_EXCERPT_VALUE";
        post.Markdown = "SECRET_BODY_VALUE";
        post.IsProtected = true;
        post.NewProtectPassword = "content-pass-1";
        var saved = await admin.Content.SaveAsync(post);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var malformed = new HttpRequestMessage(HttpMethod.Get, "/posts/protected-excerpt");
        malformed.Headers.Add("Cookie", "origami.unlock." + saved.Id!.Value.ToString("N") + "=Z");
        using var response = await client.SendAsync(malformed);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("SECRET_EXCERPT_VALUE", html);
        Assert.DoesNotContain("SECRET_BODY_VALUE", html);
        Assert.Contains("受密碼保護", html);
    }

    [PostgresFact]
    public async Task The_public_unlock_form_releases_the_content_over_http()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("表單解鎖", status: ContentStatus.Published);
        post.Slug = "unlock-form";
        post.Markdown = "SECRET_BODY_VALUE";
        post.IsProtected = true;
        post.NewProtectPassword = "content-pass-1";
        await admin.Content.SaveAsync(post);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var locked = WebUtility.HtmlDecode(await (await client.GetAsync("/posts/unlock-form")).Content.ReadAsStringAsync());
        var fields = FormFields(locked, ".protected-gate form");
        Assert.True(fields.ContainsKey("__RequestVerificationToken"));
        fields["Unlock.Password"] = "content-pass-1";
        using var unlocked = await client.PostAsync("/posts/unlock-form", new FormUrlEncodedContent(fields));
        var html = WebUtility.HtmlDecode(await unlocked.Content.ReadAsStringAsync());

        Assert.True(unlocked.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, x => x.StartsWith("origami.unlock.", StringComparison.Ordinal));
        Assert.Contains("SECRET_BODY_VALUE", html);

        // 發出的 cookie 要能在下一個請求真正開啟頁面。
        var cookie = Assert.Single(cookies, x => x.StartsWith("origami.unlock.", StringComparison.Ordinal));
        var value = cookie[(cookie.IndexOf('=') + 1)..cookie.IndexOf(';')];
        using var request = new HttpRequestMessage(HttpMethod.Get, "/posts/unlock-form");
        request.Headers.Add("Cookie", "origami.unlock." + value);
        using var opened = await client.SendAsync(request);
        Assert.Contains("SECRET_BODY_VALUE", WebUtility.HtmlDecode(await opened.Content.ReadAsStringAsync()));
    }

    [PostgresFact]
    public async Task A_locked_account_cannot_finish_the_second_factor()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        var secret = Totp.GenerateSecret();
        await using (var db = await App.CreateContextAsync())
        {
            await db.Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorEnabled, true).SetProperty(x => x.TwoFactorSecret, secret));
        }

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var loginFields = FormFields(await client.GetStringAsync("/account/login"), "form");
        loginFields["Input.Username"] = "test-admin";
        loginFields["Input.Password"] = password;
        using var firstFactor = await client.PostAsync("/account/login", new FormUrlEncodedContent(loginFields));
        var codeFields = FormFields(await firstFactor.Content.ReadAsStringAsync(), "form");
        Assert.Equal("account-code", codeFields.GetValueOrDefault("_handler"));

        // 在提交驗證碼之前，先把帳號鎖定。
        await using (var db = await App.CreateContextAsync())
        {
            await db.Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.LockoutEnabled, true)
                .SetProperty(x => x.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(15)));
        }

        var code = TotpCode(secret);
        codeFields["CodeForm.Code"] = code;
        using var secondFactor = await client.PostAsync("/account/login", new FormUrlEncodedContent(codeFields));
        using var adminPage = await client.GetAsync("/admin/posts");

        var location = secondFactor.Headers.Location?.OriginalString ?? "";
        Assert.DoesNotContain("/admin/posts", location, StringComparison.OrdinalIgnoreCase);
        Assert.True(adminPage.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized,
            "a locked account never reaches the admin area");
    }

    [PostgresFact]
    public async Task Private_and_protected_content_never_reach_the_embedding_provider()
    {
        using var admin = await App.AdministratorAsync();
        var seen = new List<string>();
        App.Embeddings.VectorFor = text =>
        {
            seen.Add(text);
            return FakeEmbeddingGateway.Basis(0);
        };

        var secret = NewContent("私密內容", status: ContentStatus.Published);
        secret.Slug = "private-embedding";
        secret.Markdown = "SECRET_PRIVATE_TEXT";
        secret.IsPrivate = true;
        var privatePost = await admin.Content.SaveAsync(secret);
        var gated = NewContent("受保護內容", status: ContentStatus.Published);
        gated.Slug = "protected-embedding";
        gated.Markdown = "SECRET_PROTECTED_TEXT";
        gated.IsProtected = true;
        gated.NewProtectPassword = "content-pass-1";
        var protectedPost = await admin.Content.SaveAsync(gated);

        // 殘留的外送匣（outbox）資料列絕不能把受保護或私密的文字送往嵌入供應商。
        await using (var db = await App.CreateContextAsync())
        {
            db.EmbeddingJobs.Add(new EmbeddingJob { ContentId = privatePost.Id!.Value, EnqueuedAt = DateTimeOffset.UtcNow, NextAttemptAt = DateTimeOffset.UtcNow });
            db.EmbeddingJobs.Add(new EmbeddingJob { ContentId = protectedPost.Id!.Value, EnqueuedAt = DateTimeOffset.UtcNow, NextAttemptAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await App.Worker.ProcessPendingAsync();

        Assert.DoesNotContain(seen, text => text.Contains("SECRET_PRIVATE_TEXT", StringComparison.Ordinal));
        Assert.DoesNotContain(seen, text => text.Contains("SECRET_PROTECTED_TEXT", StringComparison.Ordinal));
        await using var check = await App.CreateContextAsync();
        Assert.False(await check.ContentEmbeddings.AnyAsync(x => x.ContentId == privatePost.Id));
        Assert.False(await check.ContentEmbeddings.AnyAsync(x => x.ContentId == protectedPost.Id));
        Assert.False(await check.EmbeddingJobs.AnyAsync(x => x.ContentId == privatePost.Id));
        Assert.False(await check.EmbeddingJobs.AnyAsync(x => x.ContentId == protectedPost.Id));
    }

    private static string TotpCode(string base32Secret)
    {
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var key = Base32Decode(base32Secret);
        using var hmac = new HMACSHA1(key);
        var message = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            message[i] = (byte)(step & 0xff);
            step >>= 8;
        }
        var hash = hmac.ComputeHash(message);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] << 24 | hash[offset + 1] << 16 | hash[offset + 2] << 8 | hash[offset + 3]) & 0x7fffffff) % 1_000_000;
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var buffer = 0;
        var bits = 0;
        var output = new List<byte>();
        foreach (var character in value.TrimEnd('='))
        {
            var index = alphabet.IndexOf(character, StringComparison.Ordinal);
            if (index < 0) continue;
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            output.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }
        return [.. output];
    }

    private static Dictionary<string, string> FormFields(string html, string selector)
    {
        var form = new HtmlParser().ParseDocument(html).QuerySelector(selector);
        Assert.NotNull(form);
        return form.QuerySelectorAll("input[type=hidden][name]")
            .ToDictionary(x => x.GetAttribute("name")!, x => x.GetAttribute("value") ?? "");
    }
}
