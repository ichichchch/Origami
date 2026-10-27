namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProtectedServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Anonymous_direct_calls_cannot_read_or_mutate_protected_services()
    {
        using var admin = await App.AdministratorAsync();
        var post = await admin.Content.SaveAsync(NewContent("Private draft"));
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "Private category" });
        using var visitor = App.Anonymous();
        await AssertProtectedCallsRejected(visitor, post, category);
        Assert.NotNull(await admin.Content.GetEditAsync(post.Id!.Value));
        Assert.Single((await admin.Taxonomy.ListAsync(TaxonomyKind.Category)).Items);
        Assert.Null(await visitor.Content.GetPublishedAsync(ContentKind.Post, post.Slug!));
        Assert.NotNull(await visitor.Settings.GetAsync());
    }

    [PostgresFact]
    public async Task Rotating_security_stamp_immediately_rejects_the_old_actor_on_every_protected_entry()
    {
        using var oldActor = await App.AdministratorAsync();
        var post = await oldActor.Content.SaveAsync(NewContent("Private draft"));
        var category = await oldActor.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "Category" });
        using (var revoker = App.Anonymous())
        {
            var user = await revoker.Users.Users.SingleAsync();
            Assert.True((await revoker.Users.UpdateSecurityStampAsync(user)).Succeeded);
        }
        // 過程不涉及定期重新驗證、等待或替換舊的驗證狀態提供者。
        await AssertProtectedCallsRejected(oldActor, post, category);
        using var fresh = await App.AdministratorAsync();
        Assert.Equal(post.Id, (await fresh.Content.GetEditAsync(post.Id!.Value)).Id);
        Assert.Single((await fresh.Taxonomy.ListAsync(TaxonomyKind.Category)).Items);
    }

    [PostgresFact]
    public async Task Changing_the_mutable_provider_to_anonymous_revokes_an_already_resolved_service()
    {
        using var actor = await App.AdministratorAsync();
        var content = actor.Content;
        Assert.Empty((await content.ListAdminAsync(ContentKind.Post)).Items);
        actor.Authentication.SetPrincipal(new ClaimsPrincipal(new ClaimsIdentity()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => content.ListAdminAsync(ContentKind.Post));
    }

    [PostgresTheory]
    [InlineData("missing-stamp")]
    [InlineData("wrong-stamp")]
    [InlineData("missing-id")]
    [InlineData("unknown-user")]
    [InlineData("unauthenticated")]
    public async Task Forged_or_incomplete_identity_claims_do_not_grant_administrator_access(string scenario)
    {
        using var actor = await App.AdministratorAsync();
        var options = actor.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity;
        var principal = (await actor.Authentication.GetAuthenticationStateAsync()).User;
        var claims = principal.Claims.ToList();
        switch (scenario)
        {
            case "missing-stamp": claims.RemoveAll(x => x.Type == options.SecurityStampClaimType); break;
            case "wrong-stamp":
                claims.RemoveAll(x => x.Type == options.SecurityStampClaimType);
                claims.Add(new(options.SecurityStampClaimType, Guid.NewGuid().ToString()));
                break;
            case "missing-id": claims.RemoveAll(x => x.Type == options.UserIdClaimType); break;
            case "unknown-user":
                claims.RemoveAll(x => x.Type == options.UserIdClaimType);
                claims.Add(new(options.UserIdClaimType, Guid.NewGuid().ToString()));
                break;
            case "unauthenticated": break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        claims.Add(new(ClaimTypes.Role, "Administrator"));
        actor.Authentication.SetPrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims,
            scenario == "unauthenticated" ? null : "test-forged")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => actor.Content.ListAdminAsync(ContentKind.Post));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => actor.Content.SaveAsync(NewContent()));
    }

    private static async Task AssertProtectedCallsRejected(TestActor actor, ContentEdit post, TaxonomyEdit category)
    {
        var calls = new Func<Task>[]
        {
            () => actor.Content.ListAdminAsync(ContentKind.Post),
            () => actor.Content.ListAdminAsync(ContentKind.Post, status: ContentStatus.Draft),
            () => actor.Content.CountAdminAsync(ContentKind.Post),
            () => actor.Content.GetEditAsync(post.Id!.Value),
            () => actor.Content.SaveAsync(NewContent()),
            () => actor.Content.SaveAsync(post),
            () => actor.Content.DeleteAsync(post.Id!.Value, post.Version, true),
            () => actor.Taxonomy.ListAsync(TaxonomyKind.Category),
            () => actor.Taxonomy.ListAsync(TaxonomyKind.Tag),
            () => actor.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "Unauthorized category" }),
            () => actor.Taxonomy.SaveAsync(TaxonomyKind.Tag, new() { Name = "Unauthorized tag" }),
            () => actor.Taxonomy.DeleteAsync(TaxonomyKind.Category, category.Id!.Value, category.Version),
            () => actor.Taxonomy.DeleteAsync(TaxonomyKind.Tag, Guid.NewGuid(), Guid.NewGuid()),
            () => actor.Settings.SaveAsync(new() { Name = "Unauthorized settings" })
        };
        foreach (var call in calls) await Assert.ThrowsAsync<UnauthorizedAccessException>(call);
    }
}
