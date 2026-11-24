namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CliCommandTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string AdminPassword = "Cli-Admin-Pass!2026";

    [PostgresFact]
    public async Task Export_and_import_round_trip_the_default_site_without_exposing_passwords()
    {
        await App.Provisioner.ResetPasswordAsync(AdminPassword);
        using var admin = await App.AdministratorAsync();
        var post = await admin.Content.SaveAsync(NewContent("CLI 原始文章", status: ContentStatus.Published));
        var directory = TemporaryDirectory();
        var archive = Path.Combine(directory, "site.zip");
        try
        {
            await using var scope = App.Services.CreateAsyncScope();
            var exportConsole = new FakeCliConsole("test-admin", AdminPassword, "2", archive);
            Assert.Equal(0, await CliCommand.RunAsync(scope.ServiceProvider, "export", exportConsole));
            Assert.True(File.Exists(archive));
            Assert.DoesNotContain(AdminPassword, exportConsole.AllOutput, StringComparison.Ordinal);

            post.Title = "CLI 修改後文章";
            await admin.Content.SaveAsync(post);
            var importConsole = new FakeCliConsole(archive, "REPLACE");
            Assert.Equal(0, await CliCommand.RunAsync(scope.ServiceProvider, "import", importConsole));
            Assert.DoesNotContain(AdminPassword, importConsole.AllOutput, StringComparison.Ordinal);
            Assert.Contains("取代", importConsole.AllOutput, StringComparison.Ordinal);

            await using var db = await App.CreateContextAsync();
            Assert.Equal("CLI 原始文章", (await db.Contents.SingleAsync(x => x.Slug == post.Slug)).Title);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [PostgresFact]
    public async Task Create_user_uses_the_existing_password_policy_and_hides_input()
    {
        await App.Provisioner.ResetPasswordAsync(AdminPassword);
        const string newPassword = "Created-User-Pass!2026";
        var directory = TemporaryDirectory();
        try
        {
            await using var scope = App.Services.CreateAsyncScope();
            var console = new FakeCliConsole("test-admin", AdminPassword, "cli-user", "cli-user@example.test",
                newPassword, newPassword);
            Assert.Equal(0, await CliCommand.RunAsync(scope.ServiceProvider, "create-user", console));
            Assert.DoesNotContain(AdminPassword, console.AllOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(newPassword, console.AllOutput, StringComparison.Ordinal);

            await using var db = await App.CreateContextAsync();
            var user = await db.Users.SingleAsync(x => x.UserName == "cli-user");
            Assert.Equal("cli-user@example.test", user.Email);
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            Assert.True(await manager.CheckPasswordAsync(user, newPassword));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [PostgresFact]
    public async Task Sensitive_commands_refuse_redirected_input_and_wrong_admin_passwords()
    {
        await App.Provisioner.ResetPasswordAsync(AdminPassword);
        const string wrongPassword = "Wrong-Admin-Pass!2026";
        await using var scope = App.Services.CreateAsyncScope();
        var redirected = new FakeCliConsole(wrongPassword) { IsInputRedirected = true };
        Assert.Equal(2, await CliCommand.RunAsync(scope.ServiceProvider, "export", redirected));
        Assert.DoesNotContain(wrongPassword, redirected.AllOutput, StringComparison.Ordinal);

        var denied = new FakeCliConsole("test-admin", wrongPassword);
        Assert.Equal(1, await CliCommand.RunAsync(scope.ServiceProvider, "create-user", denied));
        Assert.DoesNotContain(wrongPassword, denied.AllOutput, StringComparison.Ordinal);
        Assert.Contains("管理員認證失敗", denied.AllOutput, StringComparison.Ordinal);
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "origami-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeCliConsole : CliCommand.ICliConsole
    {
        private readonly Queue<string> answers;
        private readonly StringWriter output = new();
        private readonly StringWriter error = new();
        public FakeCliConsole(params string[] answers) => this.answers = new(answers);
        public bool IsInputRedirected { get; set; }
        public TextWriter Error => error;
        public string AllOutput => output.ToString() + error.ToString();
        public void Write(string value) => output.Write(value);
        public void WriteLine(string value) => output.WriteLine(value);
        public string? ReadLine(string prompt)
        {
            output.Write(prompt);
            return answers.Count > 0 ? answers.Dequeue() : throw new InvalidOperationException("Missing CLI answer.");
        }
        public string ReadPassword(string prompt, int maximum)
        {
            output.Write(prompt);
            var value = answers.Count > 0 ? answers.Dequeue() : throw new InvalidOperationException("Missing CLI password.");
            Assert.True(value.Length <= maximum);
            return value;
        }
    }
}
