namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ExternalLoginServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresTheory]
    [InlineData("Google", "Google")]
    [InlineData("Microsoft", "Microsoft")]
    [InlineData("GitHub", "GitHub")]
    [InlineData("WeChat", "微信")]
    public async Task Link_and_unlink_rotate_stamps_revoke_old_sessions_and_pat_but_preserve_password(string provider, string displayName)
    {
        using var actor = await App.AdministratorAsync();
        var userId = await App.AdminUserIdAsync();
        var stamp = await App.AdminSecurityStampAsync();
        var credential = await actor.Credentials.CreateAsync(Credential());
        var patPrincipal = await actor.Credentials.AuthenticateAsync(credential.Token, SiteDefaults.DefaultSiteId);
        Assert.NotNull(patPrincipal);
        using var pat = App.Anonymous();
        pat.Authentication.SetPrincipal(patPrincipal);
        await pat.Access.RequireAsync();
        await using var initial = await App.CreateContextAsync();
        var passwordHash = (await initial.Users.AsNoTracking().SingleAsync()).PasswordHash;
        Assert.False(string.IsNullOrEmpty(passwordHash));

        var service = Service(actor);
        Assert.Empty(await service.ListAsync());
        var key = "test-external-key-" + provider;
        await service.LinkAsync(new(provider, key));
        Assert.NotEqual(stamp, await App.AdminSecurityStampAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LinkAsync(new(provider, "replay-key")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UnlinkAsync(provider));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => actor.Access.RequireAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pat.Access.RequireAsync());
        Assert.Null(await actor.Credentials.AuthenticateAsync(credential.Token, SiteDefaults.DefaultSiteId));

        using var fresh = await App.AdministratorAsync();
        var linked = Assert.Single(await Service(fresh).ListAsync());
        Assert.Equal(provider, linked.LoginProvider);
        Assert.Equal(displayName, linked.ProviderDisplayName);
        var serialized = JsonSerializer.Serialize(linked);
        Assert.DoesNotContain(key, serialized);
        Assert.DoesNotContain("ProviderKey", serialized, StringComparison.OrdinalIgnoreCase);
        var freshCredential = await fresh.Credentials.CreateAsync(Credential());
        var linkedStamp = await App.AdminSecurityStampAsync();
        await Service(fresh).UnlinkAsync(provider);
        Assert.NotEqual(linkedStamp, await App.AdminSecurityStampAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(fresh).UnlinkAsync(provider));
        Assert.Null(await fresh.Credentials.AuthenticateAsync(freshCredential.Token, SiteDefaults.DefaultSiteId));
        using var after = await App.AdministratorAsync();
        Assert.Empty(await Service(after).ListAsync());
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.UserLogins.ToListAsync());
        Assert.Equal(passwordHash, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId)).PasswordHash);
        var audits = await db.AuditLogs.AsNoTracking().Where(x => x.Action.StartsWith("user.external_login_")).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, entry => Assert.Equal(provider, entry.Target));
        Assert.DoesNotContain(key, JsonSerializer.Serialize(audits));
    }

    [PostgresFact]
    public async Task Same_provider_requires_unlink_first_and_another_user_cannot_take_an_existing_key()
    {
        var member = await CreateMemberAsync(SiteRole.Reader);
        using (var owner = await App.AdministratorAsync())
            await Service(owner).LinkAsync(new("Google", "shared-provider-key"));
        using var fresh = await App.AdministratorAsync();
        var stamp = await App.AdminSecurityStampAsync();
        await Assert.ThrowsAsync<CmsException>(() => Service(fresh).LinkAsync(new("Google", "replacement-key")));
        Assert.Equal(stamp, await App.AdminSecurityStampAsync());
        using var other = await App.UserAsync(member.Id);
        var otherStamp = (await other.Users.FindByIdAsync(member.Id))!.SecurityStamp;
        await Assert.ThrowsAsync<CmsException>(() => Service(other).LinkAsync(new("Google", "shared-provider-key")));
        Assert.Empty(await Service(other).ListAsync());
        await using var db = await App.CreateContextAsync();
        var stored = Assert.Single(await db.UserLogins.AsNoTracking().ToListAsync());
        Assert.Equal(await App.AdminUserIdAsync(), stored.UserId);
        Assert.Equal("shared-provider-key", stored.ProviderKey);
        Assert.Equal(otherStamp, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == member.Id)).SecurityStamp);
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "user.external_login_linked").ToListAsync());
    }

    [PostgresFact]
    public async Task Binding_is_an_account_attribute_and_does_not_grant_membership_or_promote_readers()
    {
        var site = await CreateSiteAsync();
        var member = await CreateMemberAsync(SiteRole.Reader);
        using (var actor = await App.UserAsync(member.Id))
            await Service(actor).LinkAsync(new("GitHub", "reader-github-key"));
        using var fresh = await App.UserAsync(member.Id);
        Assert.Equal(SiteRole.Reader, (await fresh.Access.GetCurrentAccessAsync()).Role);
        await Assert.ThrowsAsync<AccessDeniedException>(() => fresh.Content.SaveAsync(NewContent("Must remain reader")));
        using var otherSite = await App.UserAsync(member.Id, site.Id);
        Assert.Equal("GitHub", Assert.Single(await Service(otherSite).ListAsync()).LoginProvider);
        await Assert.ThrowsAsync<AccessDeniedException>(() => otherSite.Content.ListAdminAsync(ContentKind.Post));
        await using var db = await App.CreateContextAsync();
        var membership = Assert.Single(await db.SiteMemberships.IgnoreQueryFilters().Where(x => x.UserId == member.Id).ToListAsync());
        Assert.Equal(SiteRole.Reader, membership.Role);
        Assert.Equal(SiteDefaults.DefaultSiteId, membership.SiteId);
    }

    [PostgresTheory]
    [InlineData("anonymous")]
    [InlineData("expired")]
    [InlineData("disabled")]
    [InlineData("stale-stamp")]
    public async Task Invalid_current_identity_cannot_list_link_or_unlink(string scenario)
    {
        var member = await CreateMemberAsync(SiteRole.Reader);
        using var actor = await App.UserAsync(member.Id);
        switch (scenario)
        {
            case "anonymous": actor.Authentication.SetPrincipal(new ClaimsPrincipal(new ClaimsIdentity())); break;
            case "expired":
                var principal = (await actor.Authentication.GetAuthenticationStateAsync()).User;
                var identity = (ClaimsIdentity)principal.Identity!;
                foreach (var claim in identity.FindAll(AccessClaims.SessionExpiresAt).ToArray()) identity.RemoveClaim(claim);
                identity.AddClaim(LoginSessionLifetime.CreateClaim(DateTimeOffset.UtcNow.AddSeconds(-1)));
                break;
            case "disabled":
                using (var admin = await App.AdministratorAsync()) await admin.Sites.SetUserDisabledAsync(member.Id, true);
                break;
            case "stale-stamp":
                using (var revoker = App.Anonymous())
                {
                    var user = (await revoker.Users.FindByIdAsync(member.Id))!;
                    Assert.True((await revoker.Users.UpdateSecurityStampAsync(user)).Succeeded);
                }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var service = Service(actor);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LinkAsync(new("Google", "denied-key")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UnlinkAsync("Google"));
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.UserLogins.ToListAsync());
    }

    [PostgresTheory]
    [InlineData(ApiCredentialScope.Read)]
    [InlineData(ApiCredentialScope.Write)]
    public async Task Even_network_admin_pat_cannot_manage_external_logins(ApiCredentialScope scope)
    {
        using var actor = await App.AdministratorAsync();
        var input = Credential();
        input.Scope = scope;
        var credential = await actor.Credentials.CreateAsync(input);
        var principal = await actor.Credentials.AuthenticateAsync(credential.Token, SiteDefaults.DefaultSiteId);
        Assert.NotNull(principal);
        using var pat = App.Anonymous();
        pat.Authentication.SetPrincipal(principal);
        Assert.NotNull(await pat.Access.RequireUserAsync());
        var service = Service(pat);
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.ListAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.LinkAsync(new("Google", "denied-key")));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.UnlinkAsync("Google"));
    }

    [PostgresFact]
    public async Task Invalid_provider_keys_unknown_providers_and_missing_links_do_not_rotate_the_stamp()
    {
        using var actor = await App.AdministratorAsync();
        var service = Service(actor);
        var stamp = await App.AdminSecurityStampAsync();
        foreach (var key in new[] { "", " leading", "trailing ", "inner space", "line\nbreak", "null\0byte", new string('x', 257) })
            await Assert.ThrowsAsync<CmsException>(() => service.LinkAsync(new("Google", key)));
        foreach (var provider in new[] { "", "google", "Unknown", "Google/../GitHub" })
        {
            await Assert.ThrowsAsync<CmsException>(() => service.LinkAsync(new(provider, "key")));
            await Assert.ThrowsAsync<CmsException>(() => service.UnlinkAsync(provider));
        }
        await Assert.ThrowsAsync<CmsException>(() => service.UnlinkAsync("Google"));
        Assert.Equal(stamp, await App.AdminSecurityStampAsync());
        Assert.Empty(await service.ListAsync());
        await service.LinkAsync(new("Google", new string('k', 256)));
        using var fresh = await App.AdministratorAsync();
        Assert.Single(await Service(fresh).ListAsync());
    }

    [PostgresFact]
    public async Task List_projects_only_supported_provider_display_names_not_stored_keys_or_display_payloads()
    {
        var userId = await App.AdminUserIdAsync();
        await using (var seed = await App.CreateContextAsync())
        {
            seed.UserLogins.AddRange(
                new IdentityUserLogin<string> { UserId = userId, LoginProvider = "Google", ProviderKey = "private-key", ProviderDisplayName = "<script>stored-payload</script>" },
                new IdentityUserLogin<string> { UserId = userId, LoginProvider = "Unknown", ProviderKey = "unknown-key", ProviderDisplayName = "Unknown" });
            await seed.SaveChangesAsync();
        }
        using var actor = await App.AdministratorAsync();
        var summary = Assert.Single(await Service(actor).ListAsync());
        Assert.Equal("Google", summary.LoginProvider);
        Assert.Equal("Google", summary.ProviderDisplayName);
        var json = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain("private-key", json);
        Assert.DoesNotContain("stored-payload", json);
        Assert.DoesNotContain("ProviderKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Concurrent_links_with_the_same_original_stamp_commit_only_one_binding()
    {
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> AttemptAsync(TestActor actor, string key)
        {
            await start.Task;
            return await Record.ExceptionAsync(() => Service(actor).LinkAsync(new("Google", key)));
        }
        var a = AttemptAsync(first, "first-key");
        var b = AttemptAsync(second, "second-key");
        start.SetResult();
        var errors = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(errors, error => error is null);
        Assert.IsType<UnauthorizedAccessException>(Assert.Single(errors, error => error is not null));
        await using var db = await App.CreateContextAsync();
        Assert.Single(await db.UserLogins.ToListAsync());
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "user.external_login_linked").ToListAsync());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_after_identity_writes_rolls_back_binding_stamp_and_audit_in_the_same_transaction(bool unlink)
    {
        if (unlink)
        {
            using var seed = await App.AdministratorAsync();
            await Service(seed).LinkAsync(new("Google", "rollback-key"));
        }
        using var actor = await App.AdministratorAsync();
        var principal = (await actor.Authentication.GetAuthenticationStateAsync()).User;
        var stamp = await App.AdminSecurityStampAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<SiteContext>();
        services.AddDbContextFactory<CmsDbContext>(options => options
            .UseNpgsql(App.ConnectionString, postgresOptions => postgresOptions.UseVector())
            .AddInterceptors(new RejectExternalAudit()), ServiceLifetime.Scoped);
        services.AddCmsIdentity();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MutableAuthenticationStateProvider>();
        services.Replace(ServiceDescriptor.Scoped<AuthenticationStateProvider>(provider => provider.GetRequiredService<MutableAuthenticationStateProvider>()));
        await using var rootServices = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = rootServices.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MutableAuthenticationStateProvider>().SetPrincipal(principal);
        var service = ActivatorUtilities.CreateInstance<ExternalLoginService>(scope.ServiceProvider);
        await Assert.ThrowsAsync<CmsException>(() => unlink ? service.UnlinkAsync("Google") : service.LinkAsync(new("Google", "rollback-key")));
        // 捕捉失敗後繼續使用原 scope，不應把失敗交易留下的追蹤狀態再次儲存。
        await scope.ServiceProvider.GetRequiredService<CmsDbContext>().SaveChangesAsync();
        Assert.Equal(stamp, await App.AdminSecurityStampAsync());
        await actor.Access.RequireAsync();
        await using var db = await App.CreateContextAsync();
        Assert.Equal(unlink ? 1 : 0, await db.UserLogins.CountAsync());
        Assert.Equal(unlink ? 1 : 0, await db.AuditLogs.CountAsync(x => x.Action.StartsWith("user.external_login_")));
    }

    [PostgresFact]
    public async Task Http_metadata_surface_is_bff_only_and_revalidates_old_stamp_after_link()
    {
        using var owner = await App.AdministratorAsync();
        var credential = await owner.Credentials.CreateAsync(Credential());
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: token);
        const string path = "/api/v1/admin/profile/external-logins";
        using var anonymous = api.CreateClient();
        using var unauthenticated = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var pat = api.CreateCredentialClient(credential.Token, SiteDefaults.DefaultSiteId);
        pat.DefaultRequestHeaders.Add(AccessClaims.UserHeader, await App.AdminUserIdAsync());
        pat.DefaultRequestHeaders.Add(AccessClaims.StampHeader, await App.AdminSecurityStampAsync());
        pat.DefaultRequestHeaders.Add(AccessClaims.SessionExpiresHeader, "invalid,0");
        using (var allowed = await pat.GetAsync("/api/v1/admin/contents?kind=Post"))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, method == HttpMethod.Delete ? path + "/Google" : path);
            if (method == HttpMethod.Post) request.Content = JsonContent.Create(new ExternalLoginLinkInput("Google", "pat-key"));
            using var denied = await pat.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var bff = api.CreateBffClient(token, await App.AdminUserIdAsync(), await App.AdminSecurityStampAsync());
        using (var linked = await bff.PostAsJsonAsync(path, new ExternalLoginLinkInput("Google", "http-private-key")))
            Assert.Equal(HttpStatusCode.NoContent, linked.StatusCode);
        using (var stale = await bff.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        using (var stalePat = await pat.GetAsync("/api/v1/admin/contents?kind=Post"))
            Assert.Equal(HttpStatusCode.Unauthorized, stalePat.StatusCode);
        using var fresh = api.CreateBffClient(token, await App.AdminUserIdAsync(), await App.AdminSecurityStampAsync());
        using (var listed = await fresh.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            Assert.True(listed.Headers.CacheControl?.NoStore);
            var json = await listed.Content.ReadAsStringAsync();
            Assert.DoesNotContain("http-private-key", json);
            Assert.DoesNotContain("providerKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Google", json);
        }
        using (var removed = await fresh.DeleteAsync(path + "/Google"))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using (var replay = await fresh.DeleteAsync(path + "/Google"))
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.UserLogins.ToListAsync());
    }

    [PostgresFact]
    public async Task Personal_data_erasure_removes_external_bindings_and_invalidates_the_old_identity()
    {
        var member = await CreateMemberAsync(SiteRole.Reader);
        using (var linking = await App.UserAsync(member.Id))
            await Service(linking).LinkAsync(new("WeChat", "test-client:erased-open-id"));
        using var previousSession = await App.UserAsync(member.Id);
        using var admin = await App.AdministratorAsync();
        await admin.Sites.EraseUserPersonalDataAsync(member.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(previousSession).ListAsync());
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.UserLogins.Where(login => login.UserId == member.Id).ToListAsync());
        Assert.Null(await admin.Users.FindByLoginAsync("WeChat", "test-client:erased-open-id"));
    }

    private static ExternalLoginService Service(TestActor actor) => ActivatorUtilities.CreateInstance<ExternalLoginService>(actor.Services);

    private static CredentialCreateInput Credential() => new()
    {
        Name = "External login revocation", Scope = ApiCredentialScope.Write, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
    };

    private sealed class RejectExternalAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditLog>().Any(entry =>
                entry.State == EntityState.Added && entry.Entity.Action.StartsWith("user.external_login_", StringComparison.Ordinal)))
                throw new DbUpdateException("Injected test-only audit persistence failure.");
            return ValueTask.FromResult(result);
        }
    }
}
