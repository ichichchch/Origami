namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SiteAccessTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Ordinary_users_are_global_and_do_not_replace_the_single_bootstrap_identity()
    {
        var site = await CreateSiteAsync();
        var reader = await CreateMemberAsync(SiteRole.Reader, name: "reader");
        var editor = await CreateMemberAsync(SiteRole.Editor, site.Id, "editor");
        using var bootstrap = await App.AdministratorAsync(site.Id);
        var access = await bootstrap.Access.GetCurrentAccessAsync();
        Assert.True(access.IsNetworkAdmin);
        Assert.Equal(await App.AdminUserIdAsync(), access.UserId);
        await using var db = await App.CreateContextAsync(site.Id);
        Assert.Equal(3, await db.Users.CountAsync());
        Assert.Single(await db.Users.Where(user => user.SingletonKey == 1).ToListAsync());
        Assert.Equal(2, await db.Users.CountAsync(user => user.SingletonKey == null));
        Assert.True(await db.Users.AnyAsync(user => user.Id == reader.Id));
        Assert.True(await db.Users.AnyAsync(user => user.Id == editor.Id));
        Assert.Equal(await App.AdminSecurityStampAsync(), (await db.Users.SingleAsync(user => user.SingletonKey == 1)).SecurityStamp);
        await Assert.ThrowsAsync<AdminProvisioningException>(() =>
            App.Provisioner.InitializeAsync("another-bootstrap", IsolatedPostgres.NewPassword()));
        using var member = await App.UserAsync(reader.Id);
        Assert.False((await member.Access.GetCurrentAccessAsync()).IsNetworkAdmin);
    }

    [PostgresTheory]
    [InlineData(SiteRole.Reader)]
    [InlineData(SiteRole.Editor)]
    [InlineData(SiteRole.Administrator)]
    public async Task Roles_allow_reads_but_cap_writes_settings_and_network_management(SiteRole role)
    {
        var user = await CreateMemberAsync(role);
        using var actor = await App.UserAsync(user.Id);
        using var admin = await App.AdministratorAsync();
        var saved = await admin.Content.SaveAsync(NewContent("Visible to members"));
        Assert.Equal(saved.Id, Assert.Single((await actor.Content.ListAdminAsync(ContentKind.Post)).Items).Id);
        Assert.Equal(role, (await actor.Access.GetCurrentAccessAsync()).Role);
        if (role == SiteRole.Reader)
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.SaveAsync(NewContent("Denied")));
            await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "Denied" }));
        }
        else
            Assert.NotNull((await actor.Content.SaveAsync(NewContent("Allowed"))).Id);
        var settings = await actor.Settings.GetAsync();
        settings.Name = "Changed by member";
        if (role == SiteRole.Administrator)
            Assert.Equal(settings.Name, (await actor.Settings.SaveAsync(settings)).Name);
        else
            await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Settings.SaveAsync(settings));
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Access.RequireNetworkAdminAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Sites.ListUsersAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Sites.CreateSiteAsync(new SiteCreateInput
        {
            Name = "Denied", Slug = "denied", HostName = "denied.example.test"
        }));
        await using var db = await App.CreateContextAsync();
        Assert.Equal(role == SiteRole.Reader ? 1 : 2, await db.Contents.CountAsync());
        Assert.Equal(role == SiteRole.Administrator ? settings.Name : "Origami", (await db.Settings.SingleAsync()).Name);
        Assert.Single(await db.Sites.ToListAsync());
    }

    [PostgresFact]
    public async Task Existing_actor_observes_demotion_and_removal_without_relogin_or_stamp_rotation()
    {
        var user = await CreateMemberAsync(SiteRole.Editor);
        using var actor = await App.UserAsync(user.Id);
        using var admin = await App.AdministratorAsync();
        await actor.Content.SaveAsync(NewContent("Before demotion"));
        var originalStamp = (await actor.Users.FindByIdAsync(user.Id))!.SecurityStamp;
        var member = (await admin.Sites.ListMembersAsync()).Single(row => row.UserId == user.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = user.UserName, Role = SiteRole.Reader, Version = member.Version
        });
        Assert.Equal(SiteRole.Reader, (await actor.Access.GetCurrentAccessAsync()).Role);
        Assert.Single((await actor.Content.ListAdminAsync(ContentKind.Post)).Items);
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.SaveAsync(NewContent("After demotion")));
        member = (await admin.Sites.ListMembersAsync()).Single(row => row.UserId == user.Id);
        await admin.Sites.RemoveMemberAsync(user.Id, member.Version);
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.ListAdminAsync(ContentKind.Post));
        Assert.Null((await actor.Access.GetCurrentAccessAsync()).Role);
        await using var db = await App.CreateContextAsync();
        Assert.Equal(originalStamp, (await db.Users.SingleAsync(row => row.Id == user.Id)).SecurityStamp);
        Assert.Single(await db.Contents.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("remove")]
    [InlineData("demote")]
    [InlineData("disable")]
    public async Task Last_enabled_site_administrator_is_protected_even_when_another_admin_is_disabled(string operation)
    {
        var site = await CreateSiteAsync();
        var last = await CreateMemberAsync(SiteRole.Administrator, site.Id, "last-admin");
        var disabled = await CreateMemberAsync(SiteRole.Administrator, site.Id, "disabled-admin");
        using var network = await App.AdministratorAsync(site.Id);
        await network.Sites.SetUserDisabledAsync(disabled.Id, true);
        var bootstrapId = await App.AdminUserIdAsync();
        var bootstrap = (await network.Sites.ListMembersAsync()).Single(row => row.UserId == bootstrapId);
        await network.Sites.RemoveMemberAsync(bootstrapId, bootstrap.Version);
        var before = (await network.Sites.ListMembersAsync()).Single(row => row.UserId == last.Id);
        switch (operation)
        {
            case "remove":
                await Assert.ThrowsAsync<CmsException>(() => network.Sites.RemoveMemberAsync(last.Id, before.Version));
                break;
            case "demote":
                await Assert.ThrowsAsync<CmsException>(() => network.Sites.SetMemberAsync(new MemberEditInput
                {
                    UserName = last.UserName, Role = SiteRole.Editor, Version = before.Version
                }));
                break;
            case "disable":
                await Assert.ThrowsAsync<CmsException>(() => network.Sites.SetUserDisabledAsync(last.Id, true));
                break;
        }
        var after = (await network.Sites.ListMembersAsync()).Single(row => row.UserId == last.Id);
        Assert.Equal(before, after);
        Assert.False(after.IsDisabled);
        Assert.Equal(SiteRole.Administrator, after.Role);
        await Assert.ThrowsAsync<CmsException>(() => network.Sites.SetUserDisabledAsync(bootstrapId, true));
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_users_and_rotated_stamps_immediately_revoke_existing_sessions(bool disable)
    {
        var user = await CreateMemberAsync(SiteRole.Editor);
        using var stale = await App.UserAsync(user.Id);
        await stale.Access.RequireAsync();
        using var admin = await App.AdministratorAsync();
        if (disable) await admin.Sites.SetUserDisabledAsync(user.Id, true);
        else
        {
            var current = (await admin.Users.FindByIdAsync(user.Id))!;
            Assert.True((await admin.Users.UpdateSecurityStampAsync(current)).Succeeded);
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => stale.Content.ListAdminAsync(ContentKind.Post));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => stale.Content.SaveAsync(NewContent("Revoked")));
        var principal = (await stale.Authentication.GetAuthenticationStateAsync()).User;
        Assert.False(await AdminAccess.ValidatePrincipalAsync(principal, App.ContextFactory,
            stale.Services.GetRequiredService<IOptions<IdentityOptions>>().Value));
        if (disable) await admin.Sites.SetUserDisabledAsync(user.Id, false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => stale.Access.RequireAsync());
        using var fresh = await App.UserAsync(user.Id);
        await fresh.Access.RequireAsync();
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.Contents.ToListAsync());
    }

    [PostgresFact]
    public async Task Host_resolution_is_exact_and_only_loopback_falls_back_to_default_site()
    {
        var site = await CreateSiteAsync();
        using var anonymous = App.Anonymous();
        Assert.Equal(site.Id, (await anonymous.Sites.ResolveHostAsync("SECOND.EXAMPLE.TEST."))!.Id);
        // 未登錄網域不得退回預設站點；只有本機開發主機在預設站未綁網域時例外。
        Assert.Null(await anonymous.Sites.ResolveHostAsync("legacy.example.test"));
        Assert.Null(await anonymous.Sites.ResolveHostAsync("evil.second.example.test"));
        Assert.Equal(SiteDefaults.DefaultSiteId, (await anonymous.Sites.ResolveHostAsync("localhost"))!.Id);
        Assert.Equal(SiteDefaults.DefaultSiteId, (await anonymous.Sites.ResolveHostAsync("127.0.0.1"))!.Id);
        using var admin = await App.AdministratorAsync();
        var disabled = await admin.Sites.UpdateSiteAsync(site.Id, new SiteUpdateInput
        {
            Name = site.Name, HostName = site.HostName, IsActive = false, Version = site.Version
        });
        Assert.Null(await anonymous.Sites.ResolveHostAsync(site.HostName!));
        Assert.Null(await anonymous.Sites.ResolveHostAsync("unknown.example.test"));
        Assert.Equal(SiteDefaults.DefaultSiteId, (await anonymous.Sites.ResolveHostAsync("localhost"))!.Id);
        var defaults = (await admin.Sites.ListSitesAsync()).Single(row => row.Id == SiteDefaults.DefaultSiteId);
        await admin.Sites.UpdateSiteAsync(defaults.Id, new SiteUpdateInput
        {
            Name = defaults.Name, HostName = "default.example.test", Version = defaults.Version
        });
        Assert.Null(await anonymous.Sites.ResolveHostAsync("unknown.example.test"));
        Assert.Null(await anonymous.Sites.ResolveHostAsync("evil.default.example.test"));
        // 預設站已登錄網域後，loopback 也不再退回預設站點。
        Assert.Null(await anonymous.Sites.ResolveHostAsync("localhost"));
        Assert.Equal(defaults.Id, (await anonymous.Sites.ResolveHostAsync("DEFAULT.EXAMPLE.TEST"))!.Id);
        using var inactiveActor = await App.AdministratorAsync(disabled.Id);
        await Assert.ThrowsAsync<AccessDeniedException>(() => inactiveActor.Access.RequireAsync());
    }

    [PostgresFact]
    public async Task Disabled_users_cannot_be_registered_or_promoted_as_site_members()
    {
        var paused = await CreateMemberAsync(SiteRole.Editor, name: "paused");
        using var admin = await App.AdministratorAsync();
        await admin.Sites.SetUserDisabledAsync(paused.Id, true);
        var member = (await admin.Sites.ListMembersAsync()).Single(row => row.UserId == paused.Id);
        // 停用成員不可升級為站點管理員，也不可調整角色。
        await Assert.ThrowsAsync<CmsException>(() => admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = paused.UserName, Role = SiteRole.Administrator, Version = member.Version
        }));
        // 停用帳號不可登記為新成員。
        var outsider = await admin.Sites.CreateUserAsync(new UserCreateInput
        {
            UserName = "outsider", Password = IsolatedPostgres.NewPassword()
        });
        await admin.Sites.SetUserDisabledAsync(outsider.Id, true);
        await Assert.ThrowsAsync<CmsException>(() => admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = "outsider", Role = SiteRole.Reader
        }));
        // 停用成員仍可被移除；恢復啟用後即可正常調整角色。
        await admin.Sites.RemoveMemberAsync(paused.Id, member.Version);
        await admin.Sites.SetUserDisabledAsync(paused.Id, false);
        await admin.Sites.SetMemberAsync(new MemberEditInput { UserName = paused.UserName, Role = SiteRole.Reader });
        await using var db = await App.CreateContextAsync();
        Assert.Equal(SiteRole.Reader, (await db.SiteMemberships.IgnoreQueryFilters()
            .SingleAsync(row => row.UserId == paused.Id)).Role);
        Assert.Empty(await db.SiteMemberships.IgnoreQueryFilters()
            .Where(row => row.UserId == outsider.Id).ToListAsync());
    }

    [Theory]
    [InlineData("https://second.example.test")]
    [InlineData("second.example.test:443")]
    [InlineData("*.example.test")]
    [InlineData("second.example.test/path")]
    [InlineData("second.example.test,default.example.test")]
    [InlineData(" second.example.test")]
    public void Host_normalization_rejects_urls_ports_wildcards_and_ambiguous_inputs(string host) =>
        Assert.Throws<CmsException>(() => SiteAdministrationService.NormalizeHost(host));
}
