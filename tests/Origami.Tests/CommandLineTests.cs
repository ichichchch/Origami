namespace Origami.Tests;

/// <summary>
/// Verifies the server-side commands. The schema is applied to a genuinely empty database and
/// the refusal paths are checked without ever exposing credentials.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CommandLineTests(IsolatedPostgres postgres)
{
    [PostgresFact]
    public async Task Migrate_command_applies_the_schema_to_an_empty_database()
    {
        var empty = await CreateEmptyDatabaseAsync();
        try
        {
            var result = await RunWebCommandAsync("migrate", empty.ConnectionString, empty.StorageRoot);
            Assert.True(result.ExitCode == 0, "migrate must succeed on an empty database: " + result.Output);
            await using var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>().UseNpgsql(empty.ConnectionString, o => o.UseVector()).Options);
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.NotEmpty(applied);
            Assert.False(await db.Users.AnyAsync(), "a migrated database must not contain an administrator.");
            Assert.Equal(1, await db.Settings.CountAsync());
            // A second run must not fail and must not drop the stored data.
            await using (var seed = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>().UseNpgsql(empty.ConnectionString, o => o.UseVector()).Options))
            {
                var settings = await seed.Settings.SingleAsync();
                settings.Name = "Kept";
                await seed.SaveChangesAsync();
            }
            var second = await RunWebCommandAsync("migrate", empty.ConnectionString, empty.StorageRoot);
            Assert.True(second.ExitCode == 0, second.Output);
            await using var check = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>().UseNpgsql(empty.ConnectionString, o => o.UseVector()).Options);
            Assert.Equal("Kept", (await check.Settings.SingleAsync()).Name);
        }
        finally { await postgres.DropDatabaseAsync(empty.ConnectionString); }
    }

    [PostgresFact]
    public async Task Admin_commands_refuse_extra_arguments_and_redirected_console_input()
    {
        var empty = await CreateEmptyDatabaseAsync();
        try
        {
            const string secret = "Str0ng-Admin-Pass!2026";
            foreach (var command in new[] { "admin-init", "admin-reset" })
            {
                var withArguments = await RunWebCommandAsync(command, empty.ConnectionString, empty.StorageRoot,
                    extraArguments: ["--password", secret]);
                Assert.True(withArguments.ExitCode != 0, command + " must reject credential arguments.");
                Assert.DoesNotContain(secret, withArguments.Output, StringComparison.Ordinal);
                Assert.Contains("管理命令不接受額外參數", withArguments.Output, StringComparison.Ordinal);

                var redirected = await RunWebCommandAsync(command, empty.ConnectionString, empty.StorageRoot, redirectedInput: secret);
                Assert.True(redirected.ExitCode != 0, command + " must reject redirected console input.");
                Assert.DoesNotContain(secret, redirected.Output, StringComparison.Ordinal);
                Assert.Contains("互動終端機", redirected.Output, StringComparison.Ordinal);
            }
            // A refused command must not create an administrator.
            Assert.True((await RunWebCommandAsync("migrate", empty.ConnectionString, empty.StorageRoot)).ExitCode == 0);
            await using var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>().UseNpgsql(empty.ConnectionString, o => o.UseVector()).Options);
            Assert.False(await db.Users.AnyAsync());
        }
        finally { await postgres.DropDatabaseAsync(empty.ConnectionString); }
    }

    [Fact]
    public async Task The_application_refuses_to_start_without_a_configured_database()
    {
        var result = await RunWebCommandAsync("migrate", null, null);
        Assert.True(result.ExitCode != 0, "the application must not start without a configured database.");
        Assert.Contains("缺少 ConnectionStrings__CMS 設定", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", result.Output, StringComparison.Ordinal);
    }

    private async Task<(string ConnectionString, string StorageRoot)> CreateEmptyDatabaseAsync()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        return (connectionString, Path.Combine(Path.GetTempPath(), "origami-cmd-" + Guid.NewGuid().ToString("N")));
    }

    private static async Task<(int ExitCode, string Output)> RunWebCommandAsync(
        string command, string? connectionString, string? storageRoot, string[]? extraArguments = null, string? redirectedInput = null)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(WebApplicationPath());
        start.ArgumentList.Add(command);
        if (extraArguments is not null) foreach (var argument in extraArguments) start.ArgumentList.Add(argument);
        if (connectionString is not null) start.Environment["ConnectionStrings__Cms"] = connectionString;
        if (storageRoot is not null) start.Environment["Storage__RootPath"] = storageRoot;
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (redirectedInput is not null) await process.StandardInput.WriteLineAsync(redirectedInput);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The command did not finish within the expected time.");
        }
        return (process.ExitCode, await output + await error);
    }

    internal static string WebApplicationPath()
    {
        // The test assembly lives beside the project it references, so the configuration
        // follows the current build instead of a hard-coded folder.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Origami.slnx")))
                return Path.Combine(directory.FullName, "src", "Origami.Web", "bin", configuration, "net10.0", "Origami.Web.dll");
        throw new InvalidOperationException("Cannot locate the built web application.");
    }
}
