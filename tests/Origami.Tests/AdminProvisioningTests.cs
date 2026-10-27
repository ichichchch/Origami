namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AdminProvisioningTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    protected override bool InitializeAdministrator => false;

    [PostgresFact]
    public async Task Initialize_creates_one_hashed_admin_and_rejects_another_username()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.InitializeAsync("  first-admin  ", password, CancellationToken.None);
        using var actor = App.Anonymous();
        var user = await actor.Users.Users.SingleAsync();
        Assert.Equal("first-admin", user.UserName);
        Assert.Equal(1, user.SingletonKey);
        Assert.True(user.LockoutEnabled);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.False(string.Equals(password, user.PasswordHash, StringComparison.Ordinal));
        Assert.True(await actor.Users.CheckPasswordAsync(user, password));
        await Assert.ThrowsAsync<AdminProvisioningException>(() =>
            App.Provisioner.InitializeAsync("second-admin", IsolatedPostgres.NewPassword(), CancellationToken.None));
        await using var db = await App.CreateContextAsync();
        Assert.Single(await db.Users.ToListAsync());
    }

    [PostgresFact]
    public async Task Concurrent_initialization_has_exactly_one_winner()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Attempt(string name)
        {
            var password = IsolatedPostgres.NewPassword();
            await gate.Task;
            return await Record.ExceptionAsync(() => App.Provisioner.InitializeAsync(name, password, CancellationToken.None));
        }
        var first = Attempt("first-admin");
        var second = Attempt("second-admin");
        gate.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, x => x is null);
        Assert.IsType<AdminProvisioningException>(Assert.Single(results, x => x is not null));
        await using var db = await App.CreateContextAsync();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, (await db.Users.SingleAsync()).SingletonKey);
    }

    [PostgresFact]
    public async Task Password_reset_replaces_password_clears_lockout_and_revokes_existing_principal()
    {
        var oldPassword = IsolatedPostgres.NewPassword();
        var newPassword = IsolatedPostgres.NewPassword();
        await App.Provisioner.InitializeAsync("test-admin", oldPassword, CancellationToken.None);
        using var oldActor = await App.AdministratorAsync();
        Assert.Empty((await oldActor.Content.ListAdminAsync(Core.Models.ContentKind.Post)).Items);
        string? oldStamp;
        using (var managerActor = App.Anonymous())
        {
            var user = await managerActor.Users.Users.SingleAsync();
            oldStamp = user.SecurityStamp;
            user.AccessFailedCount = 5;
            user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            Assert.True((await managerActor.Users.UpdateAsync(user)).Succeeded);
        }
        await App.Provisioner.ResetPasswordAsync(newPassword, CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => oldActor.Content.ListAdminAsync(Core.Models.ContentKind.Post));
        using var fresh = await App.AdministratorAsync();
        var resetUser = await fresh.Users.Users.SingleAsync();
        Assert.NotEqual(oldStamp, resetUser.SecurityStamp);
        Assert.Equal(0, resetUser.AccessFailedCount);
        Assert.Null(resetUser.LockoutEnd);
        Assert.False(await fresh.Users.CheckPasswordAsync(resetUser, oldPassword));
        Assert.True(await fresh.Users.CheckPasswordAsync(resetUser, newPassword));
        Assert.Empty((await fresh.Content.ListAdminAsync(Core.Models.ContentKind.Post)).Items);
    }

    [PostgresFact]
    public async Task Reset_without_an_administrator_is_rejected()
    {
        await Assert.ThrowsAsync<AdminProvisioningException>(() =>
            App.Provisioner.ResetPasswordAsync(IsolatedPostgres.NewPassword(), CancellationToken.None));
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("empty-name")]
    [InlineData("long-name")]
    [InlineData("invalid-name")]
    [InlineData("empty-password")]
    [InlineData("short-password")]
    [InlineData("long-password")]
    public async Task Invalid_provisioning_input_does_not_create_a_user_or_expose_the_password(string scenario)
    {
        var name = "test-admin";
        var password = IsolatedPostgres.NewPassword();
        switch (scenario)
        {
            case "empty-name": name = " \t"; break;
            case "long-name": name = new string('a', AdminProvisioner.MaximumUserNameLength + 1); break;
            case "invalid-name": name = "invalid/user"; break;
            case "empty-password": password = ""; break;
            case "short-password": password = password[..7]; break;
            case "long-password": password = LongRandomPassword(AdminProvisioner.MaximumPasswordLength + 1); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var error = await Assert.ThrowsAsync<AdminProvisioningException>(() =>
            App.Provisioner.InitializeAsync(name, password, CancellationToken.None));
        if (password.Length > 0)
            Assert.False(error.Message.Contains(password, StringComparison.Ordinal), "Provisioning diagnostics must not contain credentials.");
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [PostgresFact]
    public async Task Minimum_compliant_password_is_accepted()
    {
        // Exactly eight characters covering all four required classes.
        const string password = "Aa1!wxyz";
        await App.Provisioner.InitializeAsync("test-admin", password, CancellationToken.None);
        using var actor = App.Anonymous();
        Assert.True(await actor.Users.CheckPasswordAsync(await actor.Users.Users.SingleAsync(), password));
    }

    [PostgresFact]
    public async Task Maximum_username_and_password_lengths_are_accepted()
    {
        var name = new string('a', AdminProvisioner.MaximumUserNameLength);
        var password = LongRandomPassword(AdminProvisioner.MaximumPasswordLength);
        await App.Provisioner.InitializeAsync(name, password, CancellationToken.None);
        using var actor = App.Anonymous();
        var user = await actor.Users.Users.SingleAsync();
        Assert.Equal(name, user.UserName);
        Assert.True(await actor.Users.CheckPasswordAsync(user, password));
    }

    [PostgresFact]
    public async Task Canceled_initialization_and_reset_do_not_modify_identity()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            App.Provisioner.InitializeAsync("test-admin", IsolatedPostgres.NewPassword(), cancellation.Token));
        await using (var db = await App.CreateContextAsync()) Assert.Empty(await db.Users.ToListAsync());
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.InitializeAsync("test-admin", password, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            App.Provisioner.ResetPasswordAsync(IsolatedPostgres.NewPassword(), cancellation.Token));
        using var actor = App.Anonymous();
        Assert.True(await actor.Users.CheckPasswordAsync(await actor.Users.Users.SingleAsync(), password));
    }

    private static string LongRandomPassword(int length) =>
        string.Concat(Enumerable.Range(0, length / 68 + 1).Select(_ => IsolatedPostgres.NewPassword()))[..length];
}
