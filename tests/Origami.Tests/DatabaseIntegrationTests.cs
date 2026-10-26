namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseIntegrationTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    protected override bool InitializeAdministrator => false;

    [PostgresFact]
    public async Task Migrations_create_the_complete_schema_and_the_cluster_requires_SCRAM_on_loopback()
    {
        await using var db = await App.CreateContextAsync();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(applied);
        Assert.Equal(db.Database.GetMigrations(), applied);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await db.Settings.CountAsync());
        await using var connection = new NpgsqlConnection(App.ConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", connection))
        {
            var tables = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            foreach (var required in new[] { "AspNetUsers", "AspNetRoles", "AspNetUserClaims", "Contents", "Categories", "Tags", "ContentCategories", "ContentTags", "Settings", "MediaAssets", "__EFMigrationsHistory" })
                Assert.Contains(required, tables);
        }
        await using (var command = new NpgsqlCommand("SELECT host(inet_server_addr()), inet_server_port(), current_setting('listen_addresses'), current_setting('unix_socket_directories'), current_setting('password_encryption')", connection))
        {
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("127.0.0.1", reader.GetString(0));
            Assert.NotEqual(5432, reader.GetInt32(1));
            Assert.Equal("127.0.0.1", reader.GetString(2));
            Assert.Equal("", reader.GetString(3));
            Assert.Equal("scram-sha-256", reader.GetString(4));
        }
        await using (var command = new NpgsqlCommand("SELECT auth_method FROM pg_hba_file_rules WHERE error IS NULL", connection))
        {
            var methods = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) methods.Add(reader.GetString(0));
            Assert.NotEmpty(methods);
            Assert.All(methods, method => Assert.Equal("scram-sha-256", method));
        }
        await using (var command = new NpgsqlCommand("SELECT rolpassword LIKE 'SCRAM-SHA-256$%' FROM pg_authid WHERE rolname = current_user", connection))
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
        await using (var command = new NpgsqlCommand("SELECT data_type FROM information_schema.columns WHERE table_name = 'Contents' AND column_name IN ('CreatedAt', 'UpdatedAt', 'PublishedAt')", connection))
        {
            var types = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) types.Add(reader.GetString(0));
            Assert.Equal(3, types.Count);
            Assert.All(types, type => Assert.Equal("timestamp with time zone", type));
        }
    }

    [PostgresFact]
    public async Task An_incorrect_random_password_cannot_connect_to_the_owned_cluster()
    {
        var invalid = new NpgsqlConnectionStringBuilder(App.ConnectionString) { Password = IsolatedPostgres.NewPassword() };
        await using var connection = new NpgsqlConnection(invalid.ConnectionString);
        var error = await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync());
        Assert.Equal(PostgresErrorCodes.InvalidPassword, error.SqlState);
    }

    [PostgresTheory]
    [InlineData(1, PostgresErrorCodes.UniqueViolation)]
    [InlineData(2, PostgresErrorCodes.CheckViolation)]
    public async Task Administrator_singleton_is_enforced_by_database_constraints(int singletonKey, string sqlState)
    {
        await using (var first = await App.CreateContextAsync())
        {
            first.Users.Add(new AdminUser { UserName = "first", NormalizedUserName = "FIRST", SingletonKey = 1 });
            await first.SaveChangesAsync();
        }
        await using (var second = await App.CreateContextAsync())
        {
            second.Users.Add(new AdminUser { UserName = "second", NormalizedUserName = "SECOND", SingletonKey = singletonKey });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.Equal(sqlState, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using var verify = await App.CreateContextAsync();
        Assert.Single(await verify.Users.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("kind")]
    [InlineData("status")]
    [InlineData("published-without-date")]
    [InlineData("blank-title")]
    [InlineData("empty-slug")]
    public async Task Content_check_constraints_reject_invalid_rows_even_when_services_are_bypassed(string scenario)
    {
        await using var db = await App.CreateContextAsync();
        var now = DateTimeOffset.UtcNow;
        var row = new Content { Title = "Valid", Slug = "valid", CreatedAt = now, UpdatedAt = now };
        switch (scenario)
        {
            case "kind": row.Kind = (ContentKind)99; break;
            case "status": row.Status = (ContentStatus)99; break;
            case "published-without-date": row.Status = ContentStatus.Published; break;
            case "blank-title": row.Title = " "; break;
            case "empty-slug": row.Slug = ""; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        db.Contents.Add(row);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        await using var verify = await App.CreateContextAsync();
        Assert.Empty(await verify.Contents.ToListAsync());
    }

    [PostgresFact]
    public async Task Concurrent_cross_kind_duplicate_slugs_have_one_winner_and_one_safe_service_error()
    {
        await App.Provisioner.InitializeAsync("test-admin", IsolatedPostgres.NewPassword(), CancellationToken.None);
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Save(TestActor actor, ContentKind kind)
        {
            var input = NewContent("Concurrent same slug", kind);
            await gate.Task;
            return await Record.ExceptionAsync(() => actor.Content.SaveAsync(input));
        }
        var post = Save(first, ContentKind.Post);
        var page = Save(second, ContentKind.Page);
        gate.SetResult();
        var results = await Task.WhenAll(post, page);
        Assert.Single(results, x => x is null);
        Assert.IsType<CmsException>(Assert.Single(results, x => x is not null));
        await using var db = await App.CreateContextAsync();
        Assert.Equal("concurrent-same-slug", (await db.Contents.SingleAsync()).Slug);
    }

    [PostgresFact]
    public async Task Join_tables_reject_orphaned_foreign_keys()
    {
        await using var db = await App.CreateContextAsync();
        var categoryError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"ContentCategories\" (\"CategoriesId\", \"ContentsId\") VALUES ({Guid.NewGuid()}, {Guid.NewGuid()})"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, categoryError.SqlState);
        var tagError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"ContentTags\" (\"TagsId\", \"ContentsId\") VALUES ({Guid.NewGuid()}, {Guid.NewGuid()})"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, tagError.SqlState);
    }
}
