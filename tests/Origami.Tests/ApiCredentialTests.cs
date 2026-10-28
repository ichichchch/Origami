namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ApiCredentialTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Creation_returns_one_strict_token_and_persists_only_the_secret_hash()
    {
        var site = await CreateSiteAsync();
        var user = await CreateMemberAsync(SiteRole.Editor, site.Id);
        using var actor = await App.UserAsync(user.Id, site.Id);
        var created = await actor.Credentials.CreateAsync(Input(ApiCredentialScope.Write));
        Assert.Matches("\\Aorg_[0-9a-f]{32}\\.[0-9a-f]{64}\\z", created.Token);
        Assert.Equal(created.Id.ToString("N"), created.Token[4..36]);
        var secret = created.Token[37..];
        await using var db = await App.CreateContextAsync(site.Id);
        var row = await db.ApiCredentials.SingleAsync();
        Assert.Equal(created.Id, row.Id);
        Assert.Equal(user.Id, row.UserId);
        Assert.Equal(site.Id, row.SiteId);
        Assert.Equal(ApiCredentialScope.Write, row.Scope);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))), row.SecretHash);
        Assert.NotEqual(secret, row.SecretHash);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(row));
        var summary = Assert.Single(await actor.Credentials.ListAsync());
        Assert.Equal(created.Id, summary.Id);
        var listed = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain(secret, listed);
        Assert.DoesNotContain("SecretHash", listed);
        Assert.DoesNotContain("SecurityStamp", listed);
        var principal = await actor.Credentials.AuthenticateAsync(created.Token, site.Id);
        Assert.NotNull(principal);
        Assert.Equal(user.Id, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(site.Id.ToString("D"), principal.FindFirstValue(AccessClaims.SiteId));
        Assert.False(principal.HasClaim(AccessClaims.Bff, "true"));
    }

    [PostgresTheory]
    [InlineData(ApiCredentialScope.Read, SiteRole.Reader)]
    [InlineData(ApiCredentialScope.Write, SiteRole.Editor)]
    public async Task Credentials_cap_even_network_admins_to_their_declared_scope(ApiCredentialScope scope, SiteRole expected)
    {
        using var owner = await App.AdministratorAsync();
        var created = await owner.Credentials.CreateAsync(Input(scope));
        var principal = await owner.Credentials.AuthenticateAsync(created.Token, SiteDefaults.DefaultSiteId);
        Assert.NotNull(principal);
        using var pat = App.Anonymous();
        pat.Authentication.SetPrincipal(principal);
        var access = await pat.Access.GetCurrentAccessAsync();
        Assert.Equal(expected, access.Role);
        Assert.False(access.IsNetworkAdmin);
        Assert.True(access.CanRead);
        Assert.Equal(scope == ApiCredentialScope.Write, access.CanEdit);
        Assert.False(access.CanAdminister);
        Assert.Empty((await pat.Content.ListAdminAsync(ContentKind.Post)).Items);
        if (scope == ApiCredentialScope.Write)
            Assert.NotNull((await pat.Content.SaveAsync(NewContent("PAT post"))).Id);
        else
            await Assert.ThrowsAsync<AccessDeniedException>(() => pat.Content.SaveAsync(NewContent("PAT denied")));
        await Assert.ThrowsAsync<AccessDeniedException>(() => pat.Settings.SaveAsync(new SiteSettings()));
        await Assert.ThrowsAsync<AccessDeniedException>(() => pat.Access.RequireNetworkAdminAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => pat.Sites.ListMembersAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => pat.Credentials.ListAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => pat.Credentials.CreateAsync(Input(ApiCredentialScope.Read)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => pat.Credentials.RevokeAsync(created.Id));
    }

    [PostgresFact]
    public async Task Reader_cannot_issue_write_tokens_and_existing_write_tokens_follow_current_role_and_membership()
    {
        var user = await CreateMemberAsync(SiteRole.Editor);
        using var owner = await App.UserAsync(user.Id);
        var created = await owner.Credentials.CreateAsync(Input(ApiCredentialScope.Write));
        var principal = await owner.Credentials.AuthenticateAsync(created.Token, SiteDefaults.DefaultSiteId);
        Assert.NotNull(principal);
        using var pat = App.Anonymous();
        pat.Authentication.SetPrincipal(principal);
        await pat.Access.RequireAsync();
        using var admin = await App.AdministratorAsync();
        var member = (await admin.Sites.ListMembersAsync()).Single(row => row.UserId == user.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = user.UserName, Role = SiteRole.Reader, Version = member.Version
        });
        Assert.NotNull(await owner.Credentials.AuthenticateAsync(created.Token, SiteDefaults.DefaultSiteId));
        Assert.Equal(SiteRole.Reader, (await pat.Access.GetCurrentAccessAsync()).Role);
        await Assert.ThrowsAsync<AccessDeniedException>(() => pat.Access.RequireAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => owner.Credentials.CreateAsync(Input(ApiCredentialScope.Write)));
        Assert.NotNull(await owner.Credentials.CreateAsync(Input(ApiCredentialScope.Read)));
        member = (await admin.Sites.ListMembersAsync()).Single(row => row.UserId == user.Id);
        await admin.Sites.RemoveMemberAsync(user.Id, member.Version);
        Assert.Null(await owner.Credentials.AuthenticateAsync(created.Token, SiteDefaults.DefaultSiteId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pat.Access.RequireRoleAsync(SiteRole.Reader));
    }

    [PostgresFact]
    public async Task Malformed_wrong_secret_and_wrong_site_tokens_never_authenticate()
    {
        var site = await CreateSiteAsync();
        using var actor = await App.AdministratorAsync();
        var created = await actor.Credentials.CreateAsync(Input(ApiCredentialScope.Read));
        Assert.NotNull(await actor.Credentials.AuthenticateAsync(created.Token, SiteDefaults.DefaultSiteId));
        var wrongSecret = created.Token[..^1] + (created.Token[^1] == '0' ? '1' : '0');
        foreach (var token in new[]
        {
            "", created.Token + " ", " " + created.Token, created.Token.ToUpperInvariant(),
            created.Token[..^1], created.Token + "0", created.Token.Replace('.', ':'),
            "org_" + created.Id.ToString("D") + "." + created.Token[37..],
            "org_" + Guid.Empty.ToString("N") + "." + created.Token[37..],
            created.Token[..37] + new string('z', 64), wrongSecret, new string('x', 257)
        })
            Assert.Null(await actor.Credentials.AuthenticateAsync(token, SiteDefaults.DefaultSiteId));
        Assert.Null(await actor.Credentials.AuthenticateAsync(created.Token, site.Id));
        Assert.Null(await actor.Credentials.AuthenticateAsync(created.Token, Guid.Empty));
        Assert.Null(await actor.Credentials.AuthenticateAsync(created.Token, Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task Expiry_is_required_bounded_and_exclusive_at_the_exact_instant()
    {
        using var actor = await App.AdministratorAsync();
        var clock = new CredentialClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var factory = actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>();
        var service = new ApiCredentialService(factory, actor.Access, clock);
        foreach (var expiration in new[] { default(DateTimeOffset), clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(-1), clock.GetUtcNow().AddDays(366) })
            await Assert.ThrowsAsync<CmsException>(() => service.CreateAsync(new CredentialCreateInput
            {
                Name = "Invalid expiry", ExpiresAt = expiration
            }));
        var created = await service.CreateAsync(new CredentialCreateInput
        {
            Name = "Boundary", ExpiresAt = clock.GetUtcNow().AddDays(365)
        });
        clock.UtcNow = created.ExpiresAt.AddTicks(-10);
        var principal = await service.AuthenticateAsync(created.Token, SiteDefaults.DefaultSiteId);
        Assert.NotNull(principal);
        clock.UtcNow = created.ExpiresAt;
        Assert.Null(await service.AuthenticateAsync(created.Token, SiteDefaults.DefaultSiteId));
        using var pat = App.Anonymous();
        pat.Authentication.SetPrincipal(principal);
        var access = new AdminAccess(pat.Authentication, pat.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            pat.Services.GetRequiredService<IOptions<IdentityOptions>>(), clock);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.RequireRoleAsync(SiteRole.Reader));
    }

    [PostgresTheory]
    [InlineData("revoke")]
    [InlineData("stamp")]
    [InlineData("user")]
    [InlineData("site")]
    public async Task Revocation_stamp_rotation_and_inactive_user_or_site_revalidate_existing_principals(string reason)
    {
        var site = await CreateSiteAsync();
        var user = await CreateMemberAsync(SiteRole.Editor, site.Id);
        using var actor = await App.UserAsync(user.Id, site.Id);
        var created = await actor.Credentials.CreateAsync(Input(ApiCredentialScope.Write));
        var principal = await actor.Credentials.AuthenticateAsync(created.Token, site.Id);
        Assert.NotNull(principal);
        using var pat = App.Anonymous(site.Id);
        pat.Authentication.SetPrincipal(principal);
        await pat.Access.RequireAsync();
        using var admin = await App.AdministratorAsync();
        switch (reason)
        {
            case "revoke":
                await actor.Credentials.RevokeAsync(created.Id);
                var revokedAt = Assert.Single(await actor.Credentials.ListAsync()).RevokedAt;
                Assert.NotNull(revokedAt);
                await actor.Credentials.RevokeAsync(created.Id);
                Assert.Equal(revokedAt, Assert.Single(await actor.Credentials.ListAsync()).RevokedAt);
                break;
            case "stamp":
                Assert.True((await admin.Users.UpdateSecurityStampAsync((await admin.Users.FindByIdAsync(user.Id))!)).Succeeded);
                break;
            case "user": await admin.Sites.SetUserDisabledAsync(user.Id, true); break;
            case "site":
                await admin.Sites.UpdateSiteAsync(site.Id, new SiteUpdateInput
                {
                    Name = site.Name, HostName = site.HostName, IsActive = false, Version = site.Version
                });
                break;
        }
        Assert.Null(await actor.Credentials.AuthenticateAsync(created.Token, site.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pat.Access.RequireAsync());
    }

    [PostgresFact]
    public async Task Twenty_active_credentials_is_an_atomic_per_user_per_site_limit_and_revocation_frees_a_slot()
    {
        Assert.Equal(20, Limits.ApiCredentialMax);
        using var actor = await App.AdministratorAsync();
        for (var i = 0; i < 19; i++) await actor.Credentials.CreateAsync(Input(ApiCredentialScope.Read));
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Attempt(TestActor contender)
        {
            await gate.Task;
            return await Record.ExceptionAsync(() => contender.Credentials.CreateAsync(Input(ApiCredentialScope.Read)));
        }
        var one = Attempt(first);
        var two = Attempt(second);
        gate.SetResult();
        var outcomes = await Task.WhenAll(one, two);
        Assert.Single(outcomes, outcome => outcome is null);
        Assert.IsType<CmsException>(Assert.Single(outcomes, outcome => outcome is not null));
        var credentials = await actor.Credentials.ListAsync();
        Assert.Equal(20, credentials.Count);
        await Assert.ThrowsAsync<CmsException>(() => actor.Credentials.CreateAsync(Input(ApiCredentialScope.Read)));
        await actor.Credentials.RevokeAsync(credentials[0].Id);
        await actor.Credentials.CreateAsync(Input(ApiCredentialScope.Read));
        Assert.Equal(20, (await actor.Credentials.ListAsync()).Count(row => row.RevokedAt is null));
        var site = await CreateSiteAsync();
        using var otherSite = await App.AdministratorAsync(site.Id);
        Assert.NotNull(await otherSite.Credentials.CreateAsync(Input(ApiCredentialScope.Read)));
        var member = await CreateMemberAsync(SiteRole.Reader);
        using var otherUser = await App.UserAsync(member.Id);
        Assert.NotNull(await otherUser.Credentials.CreateAsync(Input(ApiCredentialScope.Read)));
        Assert.Single(await otherUser.Credentials.ListAsync());
    }

    private static CredentialCreateInput Input(ApiCredentialScope scope) => new()
    {
        Name = "Test credential", Scope = scope, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
    };

    private sealed class CredentialClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
