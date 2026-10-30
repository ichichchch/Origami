namespace Origami.Tests;

public sealed class LoginSessionLifetimeTests
{
    private static readonly DateTimeOffset Deadline = new(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-62135596800L)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1893484800L)]
    [InlineData(253402300799L)]
    public void Canonical_unix_seconds_round_trip(long seconds)
    {
        var expires = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var claim = LoginSessionLifetime.CreateClaim(expires);
        Assert.Equal(AccessClaims.SessionExpiresAt, claim.Type);
        Assert.Equal(ClaimValueTypes.Integer64, claim.ValueType);
        Assert.Equal(seconds.ToString(CultureInfo.InvariantCulture), claim.Value);
        Assert.True(LoginSessionLifetime.TryRead(Principal(claim), out var actual));
        Assert.Equal(expires, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("-0")]
    [InlineData("1.0")]
    [InlineData("1e9")]
    [InlineData("1,000")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1\n")]
    [InlineData("１２３")]
    [InlineData("2030-01-01T00:00:00Z")]
    [InlineData("-62135596801")]
    [InlineData("253402300800")]
    [InlineData("9223372036854775808")]
    public void Noncanonical_or_out_of_range_deadlines_are_rejected(string value)
    {
        var principal = Principal(new Claim(AccessClaims.SessionExpiresAt, value));
        Assert.False(LoginSessionLifetime.TryRead(principal, out var actual));
        Assert.Equal(default, actual);
        Assert.False(LoginSessionLifetime.IsValid(principal, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Missing_and_duplicate_claims_including_separate_identities_are_rejected()
    {
        Assert.False(LoginSessionLifetime.TryRead(null, out _));
        Assert.False(LoginSessionLifetime.TryRead(Principal(), out _));
        var claim = LoginSessionLifetime.CreateClaim(Deadline);
        Assert.False(LoginSessionLifetime.TryRead(Principal(claim, claim), out _));
        var split = Principal(claim);
        split.AddIdentity(new ClaimsIdentity([LoginSessionLifetime.CreateClaim(Deadline.AddDays(1))]));
        Assert.False(LoginSessionLifetime.TryRead(split, out _));
    }

    [Fact]
    public void Expiry_is_exclusive_and_claim_precision_is_whole_seconds()
    {
        var principal = Principal(LoginSessionLifetime.CreateClaim(Deadline.AddTicks(9999999)));
        Assert.True(LoginSessionLifetime.TryRead(principal, out var actual));
        Assert.Equal(Deadline, actual);
        Assert.True(LoginSessionLifetime.IsValid(principal, Deadline.AddTicks(-1)));
        Assert.False(LoginSessionLifetime.IsValid(principal, Deadline));
        Assert.False(LoginSessionLifetime.IsValid(principal, Deadline.AddTicks(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_session_rechecks_expiry_before_returning_a_reused_snapshot(bool circuit)
    {
        var clock = new SessionClock(Deadline.AddHours(-8));
        var auth = new MutableAuthenticationStateProvider();
        var principal = Principal(LoginSessionLifetime.CreateClaim(Deadline));
        auth.SetPrincipal(principal);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var session = new AdminSession(accessor, auth, Options.Create(new IdentityOptions()), clock);
        if (circuit) session.UseCircuitAuthentication();
        var first = await session.GetAsync();
        Assert.NotNull(first);
        Assert.Equal("session-user", first.UserId);
        Assert.Equal("session-stamp", first.SecurityStamp);
        Assert.Equal(Deadline, first.ExpiresAt);
        clock.UtcNow = Deadline.AddTicks(-1);
        Assert.Same(first, await session.GetAsync());
        clock.UtcNow = Deadline;
        Assert.Null(await session.GetAsync());
        Assert.Null(await session.UserIdAsync());
        Assert.Null(await session.StampAsync(CancellationToken.None));
        Assert.False((await session.PrincipalAsync()).Identity!.IsAuthenticated);
        clock.UtcNow = Deadline.AddHours(1);
        Assert.Null(await session.GetAsync());
    }

    [Fact]
    public async Task Circuit_switch_does_not_reuse_http_identity_or_a_longer_deadline()
    {
        var clock = new SessionClock(Deadline.AddHours(-1));
        var httpPrincipal = Principal(LoginSessionLifetime.CreateClaim(Deadline.AddDays(30)));
        var circuitPrincipal = Principal(LoginSessionLifetime.CreateClaim(Deadline));
        var auth = new MutableAuthenticationStateProvider();
        auth.SetPrincipal(circuitPrincipal);
        var session = new AdminSession(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = httpPrincipal }
        }, auth, Options.Create(new IdentityOptions()), clock);
        Assert.Equal(Deadline.AddDays(30), (await session.GetAsync())!.ExpiresAt);
        session.UseCircuitAuthentication();
        Assert.Equal(Deadline, (await session.GetAsync())!.ExpiresAt);
        auth.SetPrincipal(new ClaimsPrincipal(new ClaimsIdentity()));
        Assert.Null(await session.GetAsync());
    }

    [Fact]
    public async Task Snapshot_cannot_mask_removed_duplicate_or_replaced_deadlines_or_cancellation()
    {
        var clock = new SessionClock(Deadline.AddHours(-1));
        var auth = new MutableAuthenticationStateProvider();
        var session = new AdminSession(new HttpContextAccessor(), auth, Options.Create(new IdentityOptions()), clock);
        auth.SetPrincipal(Principal(LoginSessionLifetime.CreateClaim(Deadline)));
        Assert.NotNull(await session.GetAsync());
        auth.SetPrincipal(Principal());
        Assert.Null(await session.GetAsync());
        auth.SetPrincipal(Principal(LoginSessionLifetime.CreateClaim(Deadline), LoginSessionLifetime.CreateClaim(Deadline)));
        Assert.Null(await session.GetAsync());
        auth.SetPrincipal(Principal(LoginSessionLifetime.CreateClaim(Deadline.AddMinutes(-30))));
        Assert.Equal(Deadline.AddMinutes(-30), (await session.GetAsync())!.ExpiresAt);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.GetAsync(cancelled.Token));
    }

    [Fact]
    public async Task Trusted_bff_requires_the_deadline_but_anonymous_bff_remains_anonymous()
    {
        var context = BffContext();
        var provider = Provider(context);
        Assert.False((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        context.Request.Headers[AccessClaims.UserHeader] = "session-user";
        context.Request.Headers[AccessClaims.StampHeader] = "session-stamp";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.GetAuthenticationStateAsync());
        context.Request.Headers[AccessClaims.SessionExpiresHeader] = LoginSessionLifetime.CreateClaim(Deadline).Value;
        var principal = (await provider.GetAuthenticationStateAsync()).User;
        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal("session-user", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(LoginSessionLifetime.TryRead(principal, out var expires));
        Assert.Equal(Deadline, expires);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1,2")]
    [InlineData("+1893484800")]
    [InlineData("253402300800")]
    public async Task Trusted_bff_rejects_malformed_deadline_headers(string header)
    {
        var context = BffContext();
        context.Request.Headers[AccessClaims.UserHeader] = "session-user";
        context.Request.Headers[AccessClaims.StampHeader] = "session-stamp";
        context.Request.Headers[AccessClaims.SessionExpiresHeader] = header;
        await Assert.ThrowsAsync<BadHttpRequestException>(() => Provider(context).GetAuthenticationStateAsync());
    }

    [Fact]
    public async Task Duplicate_deadline_headers_are_not_resolved_by_picking_one_value()
    {
        var context = BffContext();
        context.Request.Headers[AccessClaims.UserHeader] = "session-user";
        context.Request.Headers[AccessClaims.StampHeader] = "session-stamp";
        var value = LoginSessionLifetime.CreateClaim(Deadline).Value;
        context.Request.Headers[AccessClaims.SessionExpiresHeader] = new Microsoft.Extensions.Primitives.StringValues([value, value]);
        await Assert.ThrowsAsync<BadHttpRequestException>(() => Provider(context).GetAuthenticationStateAsync());
    }

    [Fact]
    public async Task Pat_ignores_all_forwarded_identity_headers_and_untrusted_headers_cannot_sign_in()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[AccessClaims.UserHeader] = "forged-user";
        context.Request.Headers[AccessClaims.StampHeader] = "forged-stamp";
        context.Request.Headers[AccessClaims.SessionExpiresHeader] = new Microsoft.Extensions.Primitives.StringValues(["garbage", "0"]);
        Assert.False((await Provider(context).GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        var pat = Principal(new Claim(AccessClaims.CredentialId, Guid.NewGuid().ToString("D")));
        context.User = pat;
        Assert.Same(pat, (await Provider(context).GetAuthenticationStateAsync()).User);
        Assert.False(LoginSessionLifetime.TryRead(pat, out _));
    }

    private static ClaimsPrincipal Principal(params Claim[] deadlines) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, "session-user"),
            new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, "session-stamp") }.Concat(deadlines), "test"));

    private static DefaultHttpContext BffContext() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AccessClaims.Bff, "true")], Origami.Api.ServiceTokenAuth.SchemeName))
    };

    private static Origami.Api.AdminPrincipalProvider Provider(HttpContext context) =>
        new(new HttpContextAccessor { HttpContext = context }, Options.Create(new IdentityOptions()));

    private sealed class SessionClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}

[Collection(PostgresCollection.Name)]
public sealed class LoginSessionAccessTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Already_resolved_core_access_and_cookie_validation_stop_at_the_exact_deadline()
    {
        using var actor = await App.AdministratorAsync();
        var principal = (await actor.Authentication.GetAuthenticationStateAsync()).User;
        Assert.True(LoginSessionLifetime.TryRead(principal, out var deadline));
        var clock = new AccessClock(deadline.AddTicks(-1));
        var factory = actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>();
        var options = actor.Services.GetRequiredService<IOptions<IdentityOptions>>();
        var access = new AdminAccess(actor.Authentication, factory, options, clock);
        var content = new ContentService(factory, access, clock);
        await access.RequireAsync();
        Assert.True(await AdminAccess.ValidatePrincipalAsync(principal, factory, options.Value, timeProvider: clock));
        clock.UtcNow = deadline;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.RequireAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => content.SaveAsync(NewContent("Expired write")));
        Assert.False(await AdminAccess.ValidatePrincipalAsync(principal, factory, options.Value, timeProvider: clock));
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.Contents.ToListAsync());
    }

    [PostgresFact]
    public async Task Pat_without_web_deadline_remains_valid_until_its_own_database_expiry()
    {
        using var actor = await App.AdministratorAsync();
        var created = await actor.Credentials.CreateAsync(new CredentialCreateInput
        {
            Name = "Own expiry", Scope = ApiCredentialScope.Write, ExpiresAt = DateTimeOffset.UtcNow.AddDays(2)
        });
        var principal = await actor.Credentials.AuthenticateAsync(created.Token, SiteDefaults.DefaultSiteId);
        Assert.NotNull(principal);
        Assert.False(LoginSessionLifetime.TryRead(principal, out _));
        using var pat = App.Anonymous();
        pat.Authentication.SetPrincipal(principal);
        var factory = pat.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>();
        var clock = new AccessClock(created.ExpiresAt.AddTicks(-10));
        var options = pat.Services.GetRequiredService<IOptions<IdentityOptions>>();
        var access = new AdminAccess(pat.Authentication, factory, options, clock);
        await access.RequireAsync();
        Assert.False(await AdminAccess.ValidatePrincipalAsync(principal, factory, options.Value, timeProvider: clock));
        clock.UtcNow = created.ExpiresAt;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.RequireAsync());
    }

    [PostgresFact]
    public async Task Http_bff_deadlines_are_required_strict_and_expired_before_any_write()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: token);
        using var bff = api.CreateBffClient(token, await App.AdminUserIdAsync(), await App.AdminSecurityStampAsync());
        using (var valid = await bff.GetAsync("/api/v1/admin/access")) Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        foreach (var (values, expected) in new (string[] Values, HttpStatusCode Expected)[]
        {
            ([], HttpStatusCode.Unauthorized),
            (["0"], HttpStatusCode.Unauthorized),
            (["garbage"], HttpStatusCode.BadRequest),
            (["253402300800"], HttpStatusCode.BadRequest),
            (["253402300799", "253402300799"], HttpStatusCode.BadRequest)
        })
        {
            bff.DefaultRequestHeaders.Remove(AccessClaims.SessionExpiresHeader);
            if (values.Length != 0) bff.DefaultRequestHeaders.TryAddWithoutValidation(AccessClaims.SessionExpiresHeader, values);
            using var denied = await bff.GetAsync("/api/v1/admin/access");
            Assert.Equal(expected, denied.StatusCode);
            using var write = await bff.PostAsJsonAsync("/api/v1/admin/contents", new
            {
                title = "Denied expired write", kind = "Post", markdown = "body", status = "Draft"
            });
            Assert.Equal(expected, write.StatusCode);
        }
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.Contents.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("password")]
    [InlineData("link")]
    [InlineData("unlink")]
    public async Task Correct_password_does_not_reset_custom_two_factor_failures(string operation)
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new ExternalAuthenticationTestSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        async Task<HttpResponseMessage> PasswordAsync() => await client.PostAsync("/test/password",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "test-admin", ["password"] = password, ["remember"] = "false"
            }));
        if (operation != "password")
        {
            using var signedIn = await PasswordAsync();
            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        }
        await using (var seed = await App.CreateContextAsync())
            await seed.Users.ExecuteUpdateAsync(update => update
                .SetProperty(user => user.TwoFactorEnabled, true)
                .SetProperty(user => user.TwoFactorSecret, "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")
                .SetProperty(user => user.EmailConfirmed, false)
                .SetProperty(user => user.PhoneNumberConfirmed, false)
                .SetProperty(user => user.LockoutEnabled, true));
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (operation == "password")
            {
                using var firstFactor = await PasswordAsync();
                Assert.Equal(HttpStatusCode.OK, firstFactor.StatusCode);
                using var failure = await client.PostAsync("/test/code", new FormUrlEncodedContent(
                    new Dictionary<string, string> { ["code"] = "badotp" }));
                Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
            }
            else
            {
                var csrf = await client.GetStringAsync("/test/csrf");
                using var failure = await client.PostAsync("/account/external/" + operation,
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["__RequestVerificationToken"] = csrf, ["provider"] = "GitHub",
                        ["password"] = password, ["code"] = "badotp"
                    }));
                Assert.Equal(HttpStatusCode.Redirect, failure.StatusCode);
                Assert.Equal("/account/external-logins?error=verification", failure.Headers.Location!.OriginalString);
            }
            await using var db = await App.CreateContextAsync();
            var user = await db.Users.AsNoTracking().SingleAsync();
            if (attempt < 5) Assert.Equal(attempt, user.AccessFailedCount);
            else Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
        }
        using var locked = await PasswordAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
    }

    private sealed class AccessClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
