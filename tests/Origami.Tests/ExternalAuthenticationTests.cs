namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ExternalAuthenticationTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string TotpSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    public static IEnumerable<object?[]> SignInCases()
    {
        foreach (var provider in new string?[] { null, "Google", "Microsoft", "GitHub", "WeChat" })
        foreach (var remember in new[] { false, true })
        foreach (var factor in new[] { "none", "totp", "recovery" })
            yield return [provider, remember, factor];
    }

    [PostgresTheory]
    [MemberData(nameof(SignInCases))]
    public async Task Password_and_all_providers_share_fixed_lifetimes_and_custom_second_factors(string? provider, bool remember, string factor)
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        if (provider is not null) await LinkAsync(provider);
        var recovery = TwoFactorService.GenerateRecoveryCodes();
        if (factor != "none")
        {
            await using var seed = await App.CreateContextAsync();
            await seed.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.TwoFactorEnabled, true)
                .SetProperty(user => user.TwoFactorSecret, TotpSecret).SetProperty(user => user.RecoveryCodeHashes, recovery.HashJoined));
        }
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString);
        site.Backchannel.WeChatTextPlain = true;
        await site.StartAsync();
        using var client = site.CreateClient();
        using var first = await FirstFactorAsync(site, client, provider, password, remember);
        HttpResponseMessage final = first;
        HttpResponseMessage? second = null;
        try
        {
            if (factor != "none")
            {
                AssertNoApplicationCookie(first);
                Assert.Contains(first.Headers.GetValues("Set-Cookie"), value => value.StartsWith(WebSignInFlow.PendingCookie + "=", StringComparison.Ordinal) && !value.StartsWith(WebSignInFlow.PendingCookie + "=;", StringComparison.Ordinal));
                using (var denied = await client.GetAsync("/test/me")) Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
                // 完成全部因子才起算期限，不沿用第一因子開始時間。
                site.Clock.Advance(TimeSpan.FromMinutes(4));
                second = await SubmitCodeAsync(client, factor == "totp" ? Code(site.Clock.GetUtcNow()) : recovery.PlainCodes[0]);
                Assert.Equal(HttpStatusCode.OK, second.StatusCode);
                final = second;
            }
            var ticket = site.ReadTicket(final);
            var issued = DateTimeOffset.FromUnixTimeSeconds(site.Clock.GetUtcNow().ToUnixTimeSeconds());
            var deadline = issued.Add(remember ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8));
            Assert.Equal(issued, ticket.Properties.IssuedUtc);
            Assert.Equal(deadline, ticket.Properties.ExpiresUtc);
            Assert.Equal(remember, ticket.Properties.IsPersistent);
            Assert.False(ticket.Properties.AllowRefresh);
            Assert.Equal(await App.AdminUserIdAsync(), ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
            Assert.True(LoginSessionLifetime.TryRead(ticket.Principal, out var claimDeadline));
            Assert.Equal(deadline, claimDeadline);
            var applicationCookie = Assert.Single(final.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-Origami.Admin=", StringComparison.Ordinal));
            var cookie = Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(applicationCookie);
            Assert.Equal(remember ? (DateTimeOffset?)deadline : null, cookie.Expires);
            Assert.True(cookie.HttpOnly);
            Assert.True(cookie.Secure);
            var cookieOptions = site.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
            Assert.False(cookieOptions.SlidingExpiration);
            foreach (var now in new[] { issued.AddMinutes(20), deadline.AddTicks(-1) })
            {
                site.Clock.Set(now);
                using var refreshed = await client.GetAsync("/test/me");
                Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
                Assert.Equal(deadline.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                    JsonNode.Parse(await refreshed.Content.ReadAsStringAsync())!["deadline"]!.GetValue<string>());
                if (refreshed.Headers.TryGetValues("Set-Cookie", out var refreshedCookies))
                    Assert.DoesNotContain(refreshedCookies, value => value.StartsWith("__Host-Origami.Admin=", StringComparison.Ordinal));
            }
            site.Clock.Set(deadline);
            using var expired = await client.GetAsync("/test/me");
            Assert.Equal(HttpStatusCode.Redirect, expired.StatusCode);
            Assert.StartsWith("/account/login", expired.Headers.Location!.PathAndQuery);
            if (factor == "recovery")
            {
                await using var db = await App.CreateContextAsync();
                var user = await db.Users.AsNoTracking().SingleAsync();
                Assert.False(TwoFactorService.TryRedeemRecoveryCode(user, recovery.PlainCodes[0]));
                Assert.True(TwoFactorService.TryRedeemRecoveryCode(user, recovery.PlainCodes[1]));
            }
        }
        finally { second?.Dispose(); }
    }

    [PostgresTheory]
    [InlineData("Google")]
    [InlineData("Microsoft")]
    [InlineData("GitHub")]
    [InlineData("WeChat")]
    public async Task Unlinked_provider_never_creates_or_merges_a_local_user_and_external_ticket_contains_no_tokens(string provider)
    {
        await using (var db = await App.CreateContextAsync())
            await db.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.Email, "same@example.test"));
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var challenge = await site.ChallengeAsync(client, provider, false);
        using var callback = await client.GetAsync(ExternalAuthenticationTestSite.Callback(provider, challenge));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/account/external/complete", callback.Headers.Location!.OriginalString);
        var ticket = site.ReadTicket(callback, IdentityConstants.ExternalScheme);
        Assert.Single(ticket.Principal.Claims);
        Assert.Equal(ExternalAuthenticationTestSite.ProviderKey(provider), ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Empty(Microsoft.AspNetCore.Authentication.AuthenticationTokenExtensions.GetTokens(ticket.Properties));
        Assert.DoesNotContain("test-access-token", JsonSerializer.Serialize(ticket.Properties.Items));
        Assert.DoesNotContain("test-refresh-token", JsonSerializer.Serialize(ticket.Properties.Items));
        using var complete = await client.GetAsync(callback.Headers.Location);
        Assert.Equal("/account/login?error=unlinked", complete.Headers.Location!.OriginalString);
        AssertNoApplicationCookie(complete);
        using var denied = await client.GetAsync("/test/me");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        await using var check = await App.CreateContextAsync();
        Assert.Single(await check.Users.ToListAsync());
        Assert.Empty(await check.UserLogins.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("missing-state")]
    [InlineData("forged-state")]
    [InlineData("missing-correlation")]
    [InlineData("expired-state")]
    [InlineData("cancelled")]
    [InlineData("cross-host")]
    public async Task Invalid_oauth_callback_never_contacts_the_provider_or_signs_in(string scenario)
    {
        await LinkAsync("GitHub");
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var challenge = await site.ChallengeAsync(client, "GitHub", false);
        var callback = ExternalAuthenticationTestSite.Callback("GitHub", challenge);
        if (scenario == "missing-state") callback = "/signin-github?code=test-code";
        if (scenario == "forged-state") callback = "/signin-github?code=test-code&state=forged";
        if (scenario == "expired-state") site.Clock.Advance(TimeSpan.FromMinutes(5));
        if (scenario == "cancelled") callback = callback.Replace("code=test-authorization-code", "error=access_denied", StringComparison.Ordinal);
        using var noCookies = site.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, callback);
        if (scenario == "cross-host") request.Headers.Host = "localhost:" + site.Address.Port;
        using var response = await (scenario == "missing-correlation" ? noCookies : client).SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/login?error=external", response.Headers.Location!.OriginalString);
        Assert.Equal(0, site.Backchannel.Calls);
        AssertNoApplicationCookie(response);
    }

    [PostgresTheory]
    [InlineData("origami:provider", "Google")]
    [InlineData("origami:origin", "https://foreign.example.test")]
    [InlineData("origami:site", "00000000-0000-0000-0000-000000000000")]
    [InlineData("origami:purpose", "unsupported")]
    [InlineData("origami:remember", "true")]
    [InlineData("missing-purpose", "")]
    [InlineData("redirect", "https://foreign.example.test")]
    public async Task Protected_state_still_requires_exact_provider_origin_site_purpose_and_return_uri(string field, string value)
    {
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var challenge = await site.ChallengeAsync(client, "GitHub", false);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(challenge.Query);
        var options = site.OAuthOptions("GitHub");
        var properties = options.StateDataFormat.Unprotect(query["state"].ToString());
        Assert.NotNull(properties);
        if (field == "redirect") properties.RedirectUri = value;
        else if (field == "missing-purpose") properties.Items.Remove("origami:purpose");
        else properties.Items[field] = value;
        var state = options.StateDataFormat.Protect(properties);
        using var response = await client.GetAsync("/signin-github?code=test-code&state=" + Uri.EscapeDataString(state));
        Assert.Equal("/account/login?error=external", response.Headers.Location!.OriginalString);
        Assert.Equal(0, site.Backchannel.Calls);
        AssertNoApplicationCookie(response);
    }

    [PostgresTheory]
    [InlineData("Google", "unavailable")]
    [InlineData("Microsoft", "invalid-json")]
    [InlineData("GitHub", "invalid-json")]
    [InlineData("WeChat", "wechat-error")]
    public async Task Provider_failures_use_fixed_errors_and_do_not_issue_application_cookies(string provider, string failure)
    {
        await LinkAsync(provider);
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString);
        site.Backchannel.Failure = failure;
        await site.StartAsync();
        using var client = site.CreateClient();
        var challenge = await site.ChallengeAsync(client, provider, true);
        using var response = await client.GetAsync(ExternalAuthenticationTestSite.Callback(provider, challenge));
        Assert.Equal("/account/login?error=external", response.Headers.Location!.OriginalString);
        AssertNoApplicationCookie(response);
        Assert.DoesNotContain("test-provider-private-error", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("test-access-token", response.Headers.ToString());
    }

    [PostgresFact]
    public async Task Callback_replay_loses_correlation_and_challenge_requires_antiforgery_and_known_provider()
    {
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using (var noCsrf = await client.PostAsync("/account/external/challenge", new FormUrlEncodedContent(new Dictionary<string, string> { ["provider"] = "GitHub" })))
            Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        var csrf = await client.GetStringAsync("/test/csrf");
        using (var unknown = await client.PostAsync("/account/external/challenge", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = csrf, ["provider"] = "Unknown"
        }))) Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var challenge = await site.ChallengeAsync(client, "GitHub", false);
        var url = ExternalAuthenticationTestSite.Callback("GitHub", challenge);
        using (var first = await client.GetAsync(url)) Assert.Equal("/account/external/complete", first.Headers.Location!.OriginalString);
        var calls = site.Backchannel.Calls;
        using var replay = await client.GetAsync(url);
        Assert.Equal("/account/login?error=external", replay.Headers.Location!.OriginalString);
        Assert.Equal(calls, site.Backchannel.Calls);
        AssertNoApplicationCookie(replay);
    }

    [PostgresTheory]
    [InlineData("expired")]
    [InlineData("stamp")]
    [InlineData("disabled")]
    [InlineData("locked")]
    [InlineData("cross-host")]
    [InlineData("legacy-v1")]
    public async Task Real_pending_state_cannot_complete_after_expiry_revocation_or_host_change(string scenario)
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using (var seed = await App.CreateContextAsync())
            await seed.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.TwoFactorEnabled, true).SetProperty(user => user.TwoFactorSecret, TotpSecret));
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString);
        await site.StartAsync();
        var cookies = new CookieContainer();
        using var client = site.CreateClient(cookies);
        using (var first = await FirstFactorAsync(site, client, null, password, false)) AssertNoApplicationCookie(first);
        if (scenario == "expired") site.Clock.Advance(WebSignInFlow.PendingLifetime);
        if (scenario == "legacy-v1")
        {
            var protection = site.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Origami.TwoFactorPending,v1").ToTimeLimitedDataProtector();
            var legacy = protection.Protect($"{await App.AdminUserIdAsync()}|{await App.AdminSecurityStampAsync()}|false", TimeSpan.FromMinutes(5));
            cookies.Add(site.Address, new System.Net.Cookie(WebSignInFlow.PendingCookie, legacy, "/") { Secure = true });
        }
        await using (var db = await App.CreateContextAsync())
        {
            if (scenario == "stamp") await db.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.SecurityStamp, Guid.NewGuid().ToString()));
            if (scenario == "disabled") await db.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.IsDisabled, true));
            if (scenario == "locked") await db.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.LockoutEnabled, true)
                .SetProperty(user => user.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(15)));
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/test/code")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = Code(site.Clock.GetUtcNow()) })
        };
        if (scenario == "cross-host") request.Headers.Host = "localhost:" + site.Address.Port;
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoApplicationCookie(response);
    }

    [PostgresTheory]
    [InlineData("Google", "none")]
    [InlineData("Microsoft", "none")]
    [InlineData("GitHub", "none")]
    [InlineData("WeChat", "none")]
    [InlineData("Google", "totp")]
    [InlineData("Microsoft", "totp")]
    [InlineData("GitHub", "totp")]
    [InlineData("WeChat", "totp")]
    [InlineData("Google", "recovery")]
    [InlineData("Microsoft", "recovery")]
    [InlineData("GitHub", "recovery")]
    [InlineData("WeChat", "recovery")]
    public async Task Http_link_and_unlink_require_csrf_confirmation_and_revoke_existing_sessions(string provider, string factor)
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        var recovery = TwoFactorService.GenerateRecoveryCodes();
        if (factor != "none") await SeedSecondFactorAsync(recovery.HashJoined);
        var recoveryIndex = 0;
        var stamp = await App.AdminSecurityStampAsync();
        var serviceToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: serviceToken);
        using var transport = api.CreateClient();
        transport.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString, transport);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var oldSession = site.CreateClient();
        string NextCode() => factor switch
        {
            "totp" => Code(site.Clock.GetUtcNow()), "recovery" => recovery.PlainCodes[recoveryIndex++], _ => ""
        };
        async Task SignInAsync(HttpClient target)
        {
            using var login = await FirstFactorAsync(site, target, null, password, false);
            if (factor == "none") site.ReadTicket(login);
            else
            {
                AssertPendingCookie(login);
                using var verified = await SubmitCodeAsync(target, NextCode());
                Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
                site.ReadTicket(verified);
            }
        }
        await SignInAsync(client);
        await SignInAsync(oldSession);
        var fields = new Dictionary<string, string> { ["provider"] = provider, ["password"] = password };
        using (var denied = await PostAccountAsync(client, "/account/external/link", fields, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        if (factor != "none")
        {
            using var denied = await PostAccountAsync(client, "/account/external/link", fields);
            AssertRedirect(denied, "/account/external-logins?error=verification");
            Assert.Equal(0, site.Backchannel.Calls);
        }
        using (var callback = await BeginLinkAsync(client, provider, password, NextCode()))
            AssertNoApplicationCookie(callback);
        using (var confirmation = await client.GetAsync("/account/external/complete"))
            AssertRedirect(confirmation, "/account/external-logins?confirm=1");
        using (var denied = await PostAccountAsync(client, "/account/external/link/complete", csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        await using (var before = await App.CreateContextAsync())
            Assert.Empty(await before.UserLogins.ToListAsync());
        Assert.Equal(stamp, await App.AdminSecurityStampAsync());

        using (var linked = await PostAccountAsync(client, "/account/external/link/complete",
            new Dictionary<string, string> { ["provider"] = "Untrusted", ["providerKey"] = "forged-key" }))
        {
            AssertRedirect(linked, "/account/login?error=changed");
            AssertNoApplicationCookie(linked);
        }
        await using (var db = await App.CreateContextAsync())
        {
            var binding = Assert.Single(await db.UserLogins.ToListAsync());
            Assert.Equal(provider, binding.LoginProvider);
            Assert.Equal(ExternalAuthenticationTestSite.ProviderKey(provider), binding.ProviderKey);
            Assert.Equal(await App.AdminUserIdAsync(), binding.UserId);
        }
        var linkedStamp = await App.AdminSecurityStampAsync();
        Assert.NotEqual(stamp, linkedStamp);
        using (var denied = await client.GetAsync("/test/me")) Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        using (var denied = await oldSession.GetAsync("/test/me")) Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);

        await SignInAsync(client);
        await SignInAsync(oldSession);
        using (var denied = await PostAccountAsync(client, "/account/external/unlink", fields, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        if (factor != "none")
        {
            using var denied = await PostAccountAsync(client, "/account/external/unlink", fields);
            AssertRedirect(denied, "/account/external-logins?error=verification");
        }
        await using (var db = await App.CreateContextAsync()) Assert.Single(await db.UserLogins.ToListAsync());
        Assert.Equal(linkedStamp, await App.AdminSecurityStampAsync());
        fields["code"] = NextCode();
        using (var unlinked = await PostAccountAsync(client, "/account/external/unlink", fields))
        {
            AssertRedirect(unlinked, "/account/login?error=changed");
            AssertNoApplicationCookie(unlinked);
        }
        Assert.NotEqual(linkedStamp, await App.AdminSecurityStampAsync());
        using (var denied = await client.GetAsync("/test/me")) Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        using (var denied = await oldSession.GetAsync("/test/me")) Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        await using var check = await App.CreateContextAsync();
        Assert.Empty(await check.UserLogins.ToListAsync());
        Assert.Single(await check.AuditLogs.Where(x => x.Action == "user.external_login_linked").ToListAsync());
        Assert.Single(await check.AuditLogs.Where(x => x.Action == "user.external_login_unlinked").ToListAsync());
        Assert.False(transport.DefaultRequestHeaders.Contains(AccessClaims.UserHeader));
        Assert.False(transport.DefaultRequestHeaders.Contains(AccessClaims.StampHeader));
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_link_ticket_cannot_follow_a_password_login_to_another_account(bool post)
    {
        var password = IsolatedPostgres.NewPassword();
        var otherPassword = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        string otherId;
        using (var admin = await App.AdministratorAsync())
        {
            otherId = (await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = "other-user", Password = otherPassword })).Id;
            await admin.Sites.SetMemberAsync(new MemberEditInput { UserName = "other-user", Role = SiteRole.Reader });
        }
        var serviceToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: serviceToken);
        using var transport = api.CreateClient();
        transport.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString, transport);
        await site.StartAsync();
        var cookies = new CookieContainer();
        using var client = site.CreateClient(cookies);
        using (var login = await FirstFactorAsync(site, client, null, password, false)) site.ReadTicket(login);
        using (var callback = await BeginLinkAsync(client, "GitHub", password))
            AssertNoApplicationCookie(callback);
        var externalName = ExternalCookieName(site);
        var externalValue = cookies.GetCookies(site.Address)[externalName]!.Value;
        using (var login = await PasswordAsync(client, "other-user", otherPassword, false))
            Assert.Equal(otherId, site.ReadTicket(login).Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(externalValue, cookies.GetCookies(site.Address)[externalName]!.Value);
        using (var me = await client.GetAsync("/test/me"))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Equal(otherId, JsonNode.Parse(await me.Content.ReadAsStringAsync())!["id"]!.GetValue<string>());
        }
        using var denied = post ? await PostAccountAsync(client, "/account/external/link/complete")
            : await client.GetAsync("/account/external/complete");
        AssertRedirect(denied, "/account/login?error=external");
        AssertNoApplicationCookie(denied);
        await using var check = await App.CreateContextAsync();
        Assert.Empty(await check.UserLogins.ToListAsync());
        Assert.Empty(await check.AuditLogs.Where(x => x.Action.StartsWith("user.external_login_")).ToListAsync());
    }

    [PostgresTheory]
    [InlineData("proof", false)]
    [InlineData("proof", true)]
    [InlineData("principal", false)]
    [InlineData("principal", true)]
    [InlineData("stamp", false)]
    [InlineData("stamp", true)]
    public async Task Http_link_completion_rejects_expired_proof_principal_or_revoked_stamp(string scenario, bool post)
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        var serviceToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: serviceToken);
        using var transport = api.CreateClient();
        transport.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString, transport);
        await site.StartAsync();
        using var client = site.CreateClient();
        using (var login = await FirstFactorAsync(site, client, null, password, false))
        {
            var ticket = site.ReadTicket(login);
            // 讓 application principal 先到期，而新取得的 external proof 仍在五分鐘內。
            if (scenario == "principal") site.Clock.Set(ticket.Properties.ExpiresUtc!.Value.AddMinutes(-2));
        }
        using (var callback = await BeginLinkAsync(client, "GitHub", password)) AssertNoApplicationCookie(callback);
        var csrf = await client.GetStringAsync("/test/csrf");
        if (scenario == "proof") site.Clock.Advance(WebSignInFlow.PendingLifetime);
        if (scenario == "principal") site.Clock.Advance(TimeSpan.FromMinutes(2));
        if (scenario == "stamp")
        {
            await using var db = await App.CreateContextAsync();
            await db.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.SecurityStamp, Guid.NewGuid().ToString()));
        }
        using var denied = post ? await client.PostAsync("/account/external/link/complete", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf })) : await client.GetAsync("/account/external/complete");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        if (!post || scenario == "proof") AssertRedirect(denied, "/account/login?error=external");
        else Assert.Equal("/account/login", new Uri(site.Address, denied.Headers.Location!).AbsolutePath);
        AssertNoApplicationCookie(denied);
        await using var check = await App.CreateContextAsync();
        Assert.Empty(await check.UserLogins.ToListAsync());
        Assert.Empty(await check.AuditLogs.Where(x => x.Action.StartsWith("user.external_login_")).ToListAsync());
    }

    [PostgresFact]
    public async Task Replayed_link_ticket_cannot_rebind_after_unlink_and_fresh_password_login()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        var serviceToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: serviceToken);
        using var transport = api.CreateClient();
        transport.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString, transport);
        await site.StartAsync();
        var cookies = new CookieContainer();
        using var client = site.CreateClient(cookies);
        using (var login = await FirstFactorAsync(site, client, null, password, false)) site.ReadTicket(login);
        using (var callback = await BeginLinkAsync(client, "GitHub", password)) AssertNoApplicationCookie(callback);
        var name = ExternalCookieName(site);
        var replayValue = cookies.GetCookies(site.Address)[name]!.Value;
        using (var linked = await PostAccountAsync(client, "/account/external/link/complete"))
            AssertRedirect(linked, "/account/login?error=changed");
        using (var login = await FirstFactorAsync(site, client, null, password, false)) site.ReadTicket(login);
        using (var unlinked = await PostAccountAsync(client, "/account/external/unlink",
            new Dictionary<string, string> { ["provider"] = "GitHub", ["password"] = password }))
            AssertRedirect(unlinked, "/account/login?error=changed");
        using (var login = await FirstFactorAsync(site, client, null, password, false)) site.ReadTicket(login);
        var stamp = await App.AdminSecurityStampAsync();
        // 還原真實 callback 簽發的 cookie；不能只測登出後缺少 application cookie 的拒絕。
        cookies.Add(site.Address, new System.Net.Cookie(name, replayValue, "/") { Secure = true, HttpOnly = true });
        using var replay = await PostAccountAsync(client, "/account/external/link/complete");
        AssertRedirect(replay, "/account/login?error=external");
        AssertNoApplicationCookie(replay);
        Assert.Equal(stamp, await App.AdminSecurityStampAsync());
        await using var check = await App.CreateContextAsync();
        Assert.Empty(await check.UserLogins.ToListAsync());
        Assert.Equal(2, await check.AuditLogs.CountAsync(x => x.Action.StartsWith("user.external_login_")));
    }

    [PostgresTheory]
    [InlineData(null)]
    [InlineData("GitHub")]
    public async Task Concurrent_recovery_redemption_issues_exactly_one_application_cookie(string? provider)
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        if (provider is not null) await LinkAsync(provider);
        var recovery = TwoFactorService.GenerateRecoveryCodes();
        await SeedSecondFactorAsync(recovery.HashJoined);
        await using var before = await App.CreateContextAsync();
        var original = await before.Users.AsNoTracking().SingleAsync();
        var saves = new UserSaveInterceptor(original.Id);
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString, saveInterceptor: saves);
        await site.StartAsync();
        using var firstClient = site.CreateClient();
        using var secondClient = site.CreateClient();
        firstClient.Timeout = secondClient.Timeout = TimeSpan.FromSeconds(30);
        foreach (var client in new[] { firstClient, secondClient })
        {
            using var pending = await FirstFactorAsync(site, client, provider, password, false);
            AssertPendingCookie(pending);
        }
        var clients = new[] { firstClient, secondClient };
        var requests = clients.Select(client => SubmitCodeAsync(client, recovery.PlainCodes[0])).ToArray();
        try
        {
            var responses = await Task.WhenAll(requests);
            Assert.Equal(2, saves.Calls);
            Assert.Equal(new[] { original.ConcurrencyStamp, original.ConcurrencyStamp }, saves.OriginalStamps.ToArray());
            var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            Assert.Equal(original.Id, site.ReadTicket(winner).Principal.FindFirstValue(ClaimTypes.NameIdentifier));
            AssertNoApplicationCookie(loser);
            for (var index = 0; index < responses.Length; index++)
            {
                using var me = await clients[index].GetAsync("/test/me");
                Assert.Equal(responses[index] == winner ? HttpStatusCode.OK : HttpStatusCode.Redirect, me.StatusCode);
            }
        }
        finally
        {
            foreach (var request in requests)
                if (request.IsCompletedSuccessfully) (await request).Dispose();
        }
        await using var check = await App.CreateContextAsync();
        var persisted = await check.Users.AsNoTracking().SingleAsync();
        Assert.NotEqual(original.ConcurrencyStamp, persisted.ConcurrencyStamp);
        Assert.Equal(original.SecurityStamp, persisted.SecurityStamp);
        Assert.True(TwoFactorService.TryRedeemRecoveryCode(original, recovery.PlainCodes[0]));
        Assert.Equal(original.RecoveryCodeHashes, persisted.RecoveryCodeHashes);
        Assert.False(TwoFactorService.TryRedeemRecoveryCode(persisted, recovery.PlainCodes[0]));
    }

    [PostgresTheory]
    [InlineData("recovery", false)]
    [InlineData("totp", false)]
    [InlineData("invalid", false)]
    [InlineData("recovery", true)]
    [InlineData("totp", true)]
    [InlineData("invalid", true)]
    public async Task Identity_save_failure_never_issues_a_cookie_or_persists_second_factor_changes(string factor, bool storageFailure)
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        var recovery = TwoFactorService.GenerateRecoveryCodes();
        await SeedSecondFactorAsync(recovery.HashJoined);
        var saves = new UserSaveInterceptor(await App.AdminUserIdAsync(), reject: true, storageFailure: storageFailure);
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString, saveInterceptor: saves);
        await site.StartAsync();
        using var client = site.CreateClient();
        using (var pending = await FirstFactorAsync(site, client, null, password, false)) AssertPendingCookie(pending);
        // 第一因子完成後才放入失敗次數，確實命中 ResetAccessFailedCount / AccessFailed 的儲存。
        await using (var seed = await App.CreateContextAsync())
            await seed.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.AccessFailedCount, 1));
        await using var before = await App.CreateContextAsync();
        var original = await before.Users.AsNoTracking().SingleAsync();
        var code = factor switch { "recovery" => recovery.PlainCodes[0], "totp" => Code(site.Clock.GetUtcNow()), _ => "invalid" };
        using var failed = await SubmitCodeAsync(client, code);
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        Assert.Equal(1, saves.Calls);
        AssertNoApplicationCookie(failed);
        Assert.Empty(await failed.Content.ReadAsStringAsync());
        using (var denied = await client.GetAsync("/test/me")) Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        using (var replay = await SubmitCodeAsync(client, code))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            AssertNoApplicationCookie(replay);
        }
        Assert.Equal(1, saves.Calls);
        await using var check = await App.CreateContextAsync();
        var persisted = await check.Users.AsNoTracking().SingleAsync();
        Assert.Equal(original.RecoveryCodeHashes, persisted.RecoveryCodeHashes);
        Assert.Equal(original.AccessFailedCount, persisted.AccessFailedCount);
        Assert.Equal(original.ConcurrencyStamp, persisted.ConcurrencyStamp);
        Assert.Equal(original.SecurityStamp, persisted.SecurityStamp);
    }

    [PostgresTheory]
    [InlineData("Google", "unavailable")]
    [InlineData("Microsoft", "invalid-json")]
    [InlineData("GitHub", "invalid-json")]
    [InlineData("WeChat", "wechat-error")]
    public async Task Authentication_diagnostics_do_not_disclose_codes_tokens_state_or_provider_errors(string provider, string failure)
    {
        using var logs = new AuthenticationLogs();
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString, loggerProvider: logs);
        site.Backchannel.Failure = failure;
        await site.StartAsync();
        using var client = site.CreateClient();
        var challenge = await site.ChallengeAsync(client, provider, false);
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(challenge.Query)["state"].ToString();
        using var response = await client.GetAsync(ExternalAuthenticationTestSite.Callback(provider, challenge));
        AssertRedirect(response, "/account/login?error=external");
        var diagnostics = string.Join("\n", logs.Messages);
        Assert.Contains("External authentication event (sensitive details omitted).", diagnostics);
        foreach (var sensitive in new[]
        {
            state, "test-authorization-code", "test-only-placeholder-secret", "test-access-token",
            "test-refresh-token", "test-provider-private-error"
        }) Assert.DoesNotContain(sensitive, diagnostics, StringComparison.Ordinal);
    }

    private sealed class AuthenticationLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }

        private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception) + exception);
        }
    }

    private async Task SeedSecondFactorAsync(string hashes)
    {
        await using var db = await App.CreateContextAsync();
        await db.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.TwoFactorEnabled, true)
            .SetProperty(user => user.TwoFactorSecret, TotpSecret).SetProperty(user => user.RecoveryCodeHashes, hashes));
    }

    private static async Task<HttpResponseMessage> BeginLinkAsync(HttpClient client, string provider, string password, string code = "")
    {
        using var challenge = await PostAccountAsync(client, "/account/external/link",
            new Dictionary<string, string> { ["provider"] = provider, ["password"] = password, ["code"] = code });
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.True(challenge.Headers.Location!.IsAbsoluteUri);
        var callback = await client.GetAsync(ExternalAuthenticationTestSite.Callback(provider, challenge.Headers.Location));
        AssertRedirect(callback, "/account/external/complete");
        return callback;
    }

    private static async Task<HttpResponseMessage> PostAccountAsync(HttpClient client, string path,
        Dictionary<string, string>? fields = null, bool csrf = true)
    {
        var form = fields is null ? new Dictionary<string, string>() : new Dictionary<string, string>(fields);
        if (csrf) form["__RequestVerificationToken"] = await client.GetStringAsync("/test/csrf");
        using var content = new FormUrlEncodedContent(form);
        return await client.PostAsync(path, content);
    }

    private static string ExternalCookieName(ExternalAuthenticationTestSite site) => site.Services
        .GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
        .Get(IdentityConstants.ExternalScheme).Cookie.Name!;

    private static void AssertRedirect(HttpResponseMessage response, string location)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(location, response.Headers.Location!.OriginalString);
    }

    private static void AssertPendingCookie(HttpResponseMessage response)
    {
        AssertNoApplicationCookie(response);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(WebSignInFlow.PendingCookie + "=", StringComparison.Ordinal)
            && !value.StartsWith(WebSignInFlow.PendingCookie + "=;", StringComparison.Ordinal));
    }

    private sealed class UserSaveInterceptor(string userId, bool reject = false, bool storageFailure = false) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public ConcurrentQueue<string?> OriginalStamps { get; } = new();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var entry = eventData.Context!.ChangeTracker.Entries<AdminUser>()
                .SingleOrDefault(entry => entry.Entity.Id == userId && entry.State == EntityState.Modified);
            if (entry is null) return result;
            var hashes = entry.Property(user => user.RecoveryCodeHashes);
            if (!reject && hashes.OriginalValue == hashes.CurrentValue) return result;
            OriginalStamps.Enqueue(entry.Property(user => user.ConcurrencyStamp).OriginalValue);
            var arrived = Interlocked.Increment(ref calls);
            if (reject) throw storageFailure
                ? new DbUpdateException("Injected test-only Identity storage failure.")
                : new DbUpdateConcurrencyException("Injected test-only Identity persistence failure.");
            // 兩個獨立 request 的 UserManager 都已更新實體，但都尚未送出 UPDATE。
            if (arrived == 2) bothArrived.TrySetResult();
            await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    private async Task LinkAsync(string provider)
    {
        using var actor = await App.AdministratorAsync();
        await ActivatorUtilities.CreateInstance<ExternalLoginService>(actor.Services)
            .LinkAsync(new(provider, ExternalAuthenticationTestSite.ProviderKey(provider)));
    }

    private static async Task<HttpResponseMessage> PasswordAsync(HttpClient client, string username, string password, bool remember)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username, ["password"] = password, ["remember"] = remember ? "true" : "false"
        });
        var response = await client.PostAsync("/test/password", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private static async Task<HttpResponseMessage> FirstFactorAsync(ExternalAuthenticationTestSite site, HttpClient client,
        string? provider, string password, bool remember)
    {
        if (provider is null) return await PasswordAsync(client, "test-admin", password, remember);
        var challenge = await site.ChallengeAsync(client, provider, remember);
        using var callback = await client.GetAsync(ExternalAuthenticationTestSite.Callback(provider, challenge));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/account/external/complete", callback.Headers.Location!.OriginalString);
        var complete = await client.GetAsync(callback.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        return complete;
    }

    private static Task<HttpResponseMessage> SubmitCodeAsync(HttpClient client, string code) => client.PostAsync("/test/code",
        new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code }));

    private static void AssertNoApplicationCookie(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            Assert.DoesNotContain(values, value => value.StartsWith("__Host-Origami.Admin=", StringComparison.Ordinal) && !value.StartsWith("__Host-Origami.Admin=;", StringComparison.Ordinal));
    }

    private static string Code(DateTimeOffset now)
    {
        var step = now.ToUnixTimeSeconds() / 30;
        var bytes = new byte[8];
        for (var index = 7; index >= 0; index--) { bytes[index] = (byte)(step & 255); step >>= 8; }
        using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes("12345678901234567890"));
        var hash = hmac.ComputeHash(bytes);
        var offset = hash[^1] & 15;
        var value = ((hash[offset] << 24 | hash[offset + 1] << 16 | hash[offset + 2] << 8 | hash[offset + 3]) & 0x7fffffff) % 1_000_000;
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }
}

public sealed class ExternalProviderCatalogTests
{
    [Theory]
    [InlineData("Google")]
    [InlineData("Microsoft")]
    [InlineData("GitHub")]
    [InlineData("WeChat")]
    public void Only_explicit_https_origins_are_allowed(string provider)
    {
        var catalog = new ExternalProviderCatalog(Configuration(ProviderSettings(provider)));
        Assert.True(catalog.IsAllowed(provider, "https://allowed.example.test"));
        Assert.True(catalog.IsAllowed(provider, "https://ALLOWED.EXAMPLE.TEST:443/"));
        Assert.Equal(provider, Assert.Single(catalog.ForOrigin("https://allowed.example.test")).Name);
        foreach (var origin in new[]
        {
            "http://allowed.example.test", "https://allowed.example.test:444", "https://evil.allowed.example.test",
            "https://allowed.example.test.evil.test", "https://allowed.example.test/path", "https://allowed.example.test?x=1",
            "https://user@allowed.example.test", "https://allowed.example.test/#fragment", " https://allowed.example.test"
        })
        {
            Assert.False(catalog.IsAllowed(provider, origin));
            Assert.Empty(catalog.ForOrigin(origin));
        }
        Assert.False(catalog.IsAllowed(provider.ToLowerInvariant(), "https://allowed.example.test"));
    }

    [Theory]
    [InlineData("Enabled", "false")]
    [InlineData("ClientId", "")]
    [InlineData("ClientSecret", "")]
    [InlineData("AllowedOrigins:0", "http://allowed.example.test")]
    [InlineData("AllowedOrigins:0", "https://*.example.test")]
    [InlineData("AllowedOrigins:0", "https://allowed.example.test/path")]
    public async Task Incomplete_or_disabled_configuration_never_registers_the_provider(string key, string value)
    {
        var settings = ProviderSettings("Google");
        settings["Authentication:External:Google:" + key] = value;
        var configuration = Configuration(settings);
        var catalog = new ExternalProviderCatalog(configuration);
        Assert.Empty(catalog.ForOrigin("https://allowed.example.test"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExternalAuthentication(configuration);
        await using var provider = services.BuildServiceProvider();
        var schemes = await provider.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        Assert.DoesNotContain(schemes, scheme => scheme.Name == "Google");
    }

    [Fact]
    public void Unconfigured_catalog_does_not_expose_any_provider()
    {
        var catalog = new ExternalProviderCatalog(Configuration(new Dictionary<string, string?>()));
        Assert.Empty(catalog.ForOrigin("https://allowed.example.test"));
        Assert.False(catalog.IsAllowed("Google", "https://allowed.example.test"));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Dictionary<string, string?> ProviderSettings(string provider)
    {
        var prefix = "Authentication:External:" + provider + ":";
        return new Dictionary<string, string?>
        {
            [prefix + "Enabled"] = "true",
            [prefix + (provider == "WeChat" ? "AppId" : "ClientId")] = "test-client",
            [prefix + (provider == "WeChat" ? "AppSecret" : "ClientSecret")] = "test-only-placeholder-secret",
            [prefix + "AllowedOrigins:0"] = "https://allowed.example.test"
        };
    }
}
