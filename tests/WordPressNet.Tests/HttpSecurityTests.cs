using System.Net;
using AngleSharp.Html.Parser;
using WordPressNet.Web.Models;
using WordPressNet.Web.Services;

namespace WordPressNet.Tests;

[Collection(PostgresCollection.Name)]
public sealed class HttpSecurityTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Repeated_failed_logins_are_throttled_and_security_headers_are_present()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var home = await client.GetAsync("/");
        Assert.Equal("nosniff", home.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", home.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", home.Headers.CacheControl!.ToString());
        HttpResponseMessage? throttled = null;
        for (var attempt = 0; attempt < LoginThrottle.MaxAttempts + 3; attempt++)
        {
            var response = await LoginAsync(client, IsolatedPostgres.NewPassword());
            if (response.StatusCode == HttpStatusCode.TooManyRequests) { throttled = response; break; }
        }
        Assert.NotNull(throttled);
        Assert.Contains("嘗試次數過多", WebUtility.HtmlDecode(await throttled!.Content.ReadAsStringAsync()));
        // Exhausting a source also refuses a correct password from that source.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, password)).StatusCode);
    }

    [PostgresFact]
    public async Task Rotating_nonexistent_usernames_cannot_bypass_source_limit_or_block_public_gets()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        for (var attempt = 0; attempt < LoginThrottle.MaxAttempts; attempt++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"192.0.2.{attempt + 1}");
            using var response = await LoginAsync(client, password, $"nonexistent-{attempt}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("無法登入", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }

        using (var actor = App.Anonymous())
        {
            var user = await actor.Users.FindByNameAsync("test-admin");
            Assert.False(await actor.Users.IsLockedOutAsync(user!));
            Assert.Equal(0, await actor.Users.GetAccessFailedCountAsync(user!));
        }

        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.1");
        using var throttled = await LoginAsync(client, password, "another-nonexistent");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        var html = WebUtility.HtmlDecode(await throttled.Content.ReadAsStringAsync());
        Assert.Contains("嘗試次數過多", html);
        Assert.False(html.Contains(password, StringComparison.Ordinal), "Throttling must never render a submitted password.");
        using var correctPassword = await LoginAsync(client, password);
        Assert.Equal(HttpStatusCode.TooManyRequests, correctPassword.StatusCode);
        using var loginPage = await client.GetAsync("/account/login");
        using var home = await client.GetAsync("/");
        using var adminPage = await client.GetAsync("/admin/posts");
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, adminPage.StatusCode);
    }

    [PostgresFact]
    public async Task Successful_login_consumes_a_permit_without_resetting_the_source_budget()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        for (var attempt = 0; attempt < LoginThrottle.MaxAttempts - 1; attempt++)
        {
            using var failed = await LoginAsync(client, password, $"nonexistent-{attempt}");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }
        using var success = await LoginAsync(client, password);
        Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
        using var anotherClient = site.CreateClient();
        using var throttled = await LoginAsync(anotherClient, password);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        using var authenticated = await client.GetAsync("/admin/posts");
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [PostgresFact]
    public async Task Anonymous_clients_are_redirected_from_every_admin_page()
    {
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        foreach (var path in new[] { "/admin/posts", "/admin/pages", "/admin/taxonomies", "/admin/media", "/admin/settings", "/admin/content/" + Guid.NewGuid() })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/account/login", response.Headers.Location!.ToString());
        }
    }

    [PostgresFact]
    public async Task An_out_of_range_public_page_number_returns_not_found()
    {
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/?page=99999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/?page=abc")).StatusCode);
    }

    [PostgresFact]
    public async Task Http_login_logout_antiforgery_secure_cookies_and_stamp_revocation()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        var cookies = new CookieContainer();
        using var client = site.CreateClient(cookies: cookies);
        var anonymous = await client.GetAsync("/admin/posts");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/account/login", anonymous.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/account/register")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/account/reset-password")).StatusCode);

        var forgedLogin = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Input.Username"] = "test-admin", ["Input.Password"] = password, ["_handler"] = "account-login" }));
        Assert.Equal(HttpStatusCode.BadRequest, forgedLogin.StatusCode);
        var login = await LoginAsync(client, password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var redirect = new Uri(site.Address, login.Headers.Location!);
        Assert.Equal(site.Address.Authority, redirect.Authority);
        Assert.Equal("/admin/posts", redirect.AbsolutePath);
        var cookieHeader = Assert.Single(login.Headers.GetValues("Set-Cookie"), h => h.StartsWith("__Host-WordPressNet.Admin="));
        Assert.Contains("secure", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.False((await login.Content.ReadAsStringAsync()).Contains(password, StringComparison.Ordinal), "Login must never render a submitted password.");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/posts")).StatusCode);

        var staleCookies = new CookieContainer();
        foreach (Cookie cookie in cookies.GetCookies(site.Address))
            staleCookies.Add(site.Address, new Cookie(cookie.Name, cookie.Value, cookie.Path) { Secure = cookie.Secure, HttpOnly = cookie.HttpOnly });
        using var staleClient = site.CreateClient(cookies: staleCookies);
        var getLogout = await client.GetAsync("/account/logout");
        Assert.True(getLogout.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/posts")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/logout", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/posts")).StatusCode);

        var adminHtml = await client.GetStringAsync("/admin/posts");
        var form = FormValues(adminHtml, "form[action='/account/logout']");
        var logout = await client.PostAsync("/account/logout", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/", logout.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin/posts")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await staleClient.GetAsync("/admin/posts")).StatusCode);
    }

    [PostgresFact]
    public async Task Public_ssr_returns_real_404_for_drafts_withdrawal_deletion_and_wrong_kind()
    {
        using var admin = await App.AdministratorAsync();
        var draft = await admin.Content.SaveAsync(NewContent("Private draft"));
        var page = await admin.Content.SaveAsync(NewContent("About", ContentKind.Page, ContentStatus.Published));
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var home = await client.GetStringAsync("/");
        Assert.DoesNotContain("Private draft", home);
        Assert.Contains("/pages/about", home);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/private-draft")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/pages/about")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/about")).StatusCode);
        draft.Status = ContentStatus.Published;
        draft.Markdown = "**Public body** <script>window.pwned=true</script> [bad](javascript:alert(1))";
        await admin.Content.SaveAsync(draft);
        var published = await client.GetAsync("/posts/private-draft");
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Contains("no-store", published.Headers.CacheControl!.ToString());
        var document = new HtmlParser().ParseDocument(await published.Content.ReadAsStringAsync());
        Assert.Contains(document.QuerySelectorAll("strong"), e => e.TextContent == "Public body");
        Assert.Empty(document.QuerySelectorAll("a[href^='javascript:']"));
        draft.Status = ContentStatus.Draft;
        await admin.Content.SaveAsync(draft);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/private-draft")).StatusCode);
        await admin.Content.DeleteAsync(draft.Id!.Value, draft.Version, confirmed: true);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/private-draft")).StatusCode);
        page.Status = ContentStatus.Draft;
        await admin.Content.SaveAsync(page);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/pages/about")).StatusCode);
        Assert.DoesNotContain("/pages/about", await client.GetStringAsync("/"));
    }

    [PostgresFact]
    public async Task Failed_login_locks_account_and_server_recovery_invalidates_cookies()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await LoginAsync(client, IsolatedPostgres.NewPassword());
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
            Assert.Contains("無法登入", WebUtility.HtmlDecode(await failed.Content.ReadAsStringAsync()));
        }
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, password)).StatusCode);
        using (var actor = App.Anonymous())
        {
            var user = await actor.Users.FindByNameAsync("test-admin");
            Assert.True(await actor.Users.IsLockedOutAsync(user!));
        }
        await App.Provisioner.ResetPasswordAsync(password);
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, password)).StatusCode);
        await App.Provisioner.ResetPasswordAsync(IsolatedPostgres.NewPassword());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin/posts")).StatusCode);
    }

    internal static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string password, string username = "test-admin")
    {
        var fields = FormValues(await client.GetStringAsync("/account/login"), "form");
        fields["Input.Username"] = username;
        fields["Input.Password"] = password;
        return await client.PostAsync("/account/login", new FormUrlEncodedContent(fields));
    }

    private static Dictionary<string, string> FormValues(string html, string selector)
    {
        var form = new HtmlParser().ParseDocument(html).QuerySelector(selector);
        Assert.NotNull(form);
        return form.QuerySelectorAll("input[type='hidden'][name]").ToDictionary(x => x.GetAttribute("name")!, x => x.GetAttribute("value") ?? "");
    }
}
