namespace Origami.Tests;

public sealed record PostgresBinaries(string InitDb, string PgCtl)
{
    public static PostgresBinaries? Find()
    {
        var configured = Environment.GetEnvironmentVariable("ORIGAMI_PG_BIN");
        IEnumerable<string> directories = !string.IsNullOrWhiteSpace(configured)
            ? [configured]
            : new[] { @"D:\PostgreSQL\bin" }.Concat(
                (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        foreach (var entry in directories.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var directory = Path.GetFullPath(entry.Trim('"'));
            var initDb = Path.Combine(directory, "initdb" + suffix);
            var pgCtl = Path.Combine(directory, "pg_ctl" + suffix);
            if (File.Exists(initDb) && File.Exists(pgCtl)) return new(initDb, pgCtl);
        }
        return null;
    }

    public const string MissingDiagnostic = "PostgreSQL integration not executed: initdb and pg_ctl are absent. " +
        "Set ORIGAMI_PG_BIN, provide D:\\PostgreSQL\\bin, or put server binaries on PATH. " +
        "No existing database or substitute provider is used.";
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (PostgresBinaries.Find() is null) Skip = PostgresBinaries.MissingDiagnostic;
    }
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (PostgresBinaries.Find() is null) Skip = PostgresBinaries.MissingDiagnostic;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<IsolatedPostgres>
{
    public const string Name = "Owned isolated PostgreSQL";
}

/// <summary>全新且僅用 SCRAM 的環回叢集；絕不讀取應用程式連線設定。</summary>
public sealed class IsolatedPostgres : IAsyncLifetime
{
    private const string TemplateDatabase = "origami_migrated_template";
    private readonly PostgresBinaries? binaries = PostgresBinaries.Find();
    private readonly string root = Path.Combine(Path.GetTempPath(), "Origami.Tests-" + Guid.NewGuid().ToString("N"));
    private readonly string password = NewPassword();
    private readonly HashSet<string> ownedDatabases = [];
    private bool ownsDirectory;
    private bool started;
    private int port;
    private string DataDirectory => Path.Combine(root, "data");
    private string LogFile => Path.Combine(root, "postgres.log");

    public static string NewPassword() => "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public async Task InitializeAsync()
    {
        // 自訂 attribute 呈現的是真正的 xUnit 跳過，而非通過的空操作測試。
        if (binaries is null) return;
        Directory.CreateDirectory(root);
        ownsDirectory = true;
        var passwordFile = Path.Combine(root, "bootstrap.pw");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var file = new FileStream(passwordFile, options))
            await using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                await writer.WriteLineAsync(password);
            try
            {
                await RunAsync(binaries.InitDb, "-D", DataDirectory, "--username=postgres",
                    "--auth-local=scram-sha-256", "--auth-host=scram-sha-256",
                    "--encoding=UTF8", "--locale=C", "--pwfile=" + passwordFile);
            }
            finally
            {
                File.Delete(passwordFile);
            }

            // 保留一個由作業系統配置的未用埠，直到 pg_ctl 啟動前一刻釋放。
            // 埠被搶佔屬於啟動失敗，絕不成為改試既有伺服器的理由。
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            if (port == 5432) throw new InvalidOperationException("Refusing reserved PostgreSQL port 5432.");
            // 只設定自行產生的叢集。改寫設定檔可避免 pg_ctl 對空 socket 目錄
            // 引數的 shell 解析在 Windows／POSIX 之間的差異。
            await File.AppendAllTextAsync(Path.Combine(DataDirectory, "postgresql.conf"),
                $"\nlisten_addresses = '127.0.0.1'\nport = {port}\nunix_socket_directories = ''\npassword_encryption = 'scram-sha-256'\n");
            await RunAsync(binaries.PgCtl, "-D", DataDirectory, "-w", "-t", "60", "-l", LogFile, "start");
            started = true;

            await ExecuteAdminAsync($"CREATE DATABASE {TemplateDatabase}");
            await using var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
                .UseNpgsql(ConnectionString(TemplateDatabase), o => o.UseVector()).Options);
            if (!db.Database.GetMigrations().Any())
                throw new InvalidOperationException("Integration blocked: no EF migrations exist. Generate the application migrations; EnsureCreated is deliberately not used.");
            await db.Database.MigrateAsync();
        }
        catch (Exception error)
        {
            var diagnostic = Sanitize(error.Message);
            // 叢集可能仍在執行並佔住日誌檔；讀取日誌絕不能掩蓋原始錯誤。
            try
            {
                if (File.Exists(LogFile))
                    using (var stream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                        diagnostic += Environment.NewLine + Sanitize(await reader.ReadToEndAsync());
            }
            catch (IOException) { /* 原始錯誤才是有用的診斷資訊。 */ }
            try { await DisposeAsync(); }
            catch (Exception cleanupError) { diagnostic += Environment.NewLine + Sanitize(cleanupError.Message); }
            // 不附加可能洩漏連線字串的內部例外。
            throw new InvalidOperationException("Isolated PostgreSQL setup failed: " + diagnostic);
        }
    }

    public async Task<string> CreateDatabaseAsync()
    {
        if (!started) throw new InvalidOperationException(PostgresBinaries.MissingDiagnostic);
        var name = "origami_test_" + Guid.NewGuid().ToString("N");
        await ExecuteAdminAsync($"CREATE DATABASE {name} TEMPLATE {TemplateDatabase}");
        ownedDatabases.Add(name);
        return ConnectionString(name);
    }

    /// <summary>建立一個不含任何結構的資料庫，用於驗證遷移命令。</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        if (!started) throw new InvalidOperationException(PostgresBinaries.MissingDiagnostic);
        var name = "origami_empty_" + Guid.NewGuid().ToString("N");
        await ExecuteAdminAsync($"CREATE DATABASE {name}");
        ownedDatabases.Add(name);
        return ConnectionString(name);
    }

    /// <summary>此自持叢集的管理連線字串。</summary>
    public string ConnectionStringForAdmin => ConnectionString("postgres");

    public async Task DropDatabaseAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var name = builder.Database ?? throw new InvalidOperationException("Missing owned database name.");
        if (builder.Host != "127.0.0.1" || builder.Port != port || !ownedDatabases.Contains(name))
            throw new InvalidOperationException("Refusing to drop a database not created by this fixture.");
        await ExecuteAdminAsync($"DROP DATABASE {name}");
        ownedDatabases.Remove(name);
    }

    private string ConnectionString(string database) => new NpgsqlConnectionStringBuilder
    {
        Host = "127.0.0.1", Port = port, Database = database, Username = "postgres", Password = password,
        Pooling = false, Timeout = 10, CommandTimeout = 30, IncludeErrorDetail = false,
        ApplicationName = "Origami isolated tests"
    }.ConnectionString;

    private async Task ExecuteAdminAsync(string sql)
    {
        // 上方的識別字都是 fixture 產生的小寫 ASCII，絕非使用者輸入。
        await using var connection = new NpgsqlConnection(ConnectionString("postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private string Sanitize(string text) => text.Replace(password, "[redacted]", StringComparison.Ordinal);

    private async Task RunAsync(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = root
        };
        foreach (var key in info.Environment.Keys.Where(x => x.StartsWith("PG", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.Environment.Remove(key);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new StringBuilder();
        void Capture(object sender, DataReceivedEventArgs args)
        {
            if (args.Data is null) return;
            lock (captured)
            {
                if (captured.Length < 128_000) captured.AppendLine(Sanitize(args.Data));
            }
        }
        process.OutputDataReceived += Capture;
        process.ErrorDataReceived += Capture;
        process.Exited += (_, _) => exited.TrySetResult();
        if (!process.Start()) throw new InvalidOperationException("Could not start PostgreSQL tool.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            // 等待 pg_ctl 本身結束，而不是等常駐 Windows postgres 處理程序
            // 繼承的管線出現 EOF；pg_ctl -w 仍是就緒與否的權威判準。
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(90));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            throw new TimeoutException(Path.GetFileName(executable) + " exceeded the isolated-cluster startup/shutdown timeout.");
        }
        finally
        {
            process.CancelOutputRead();
            process.CancelErrorRead();
        }
        string output;
        lock (captured) output = captured.ToString();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(executable)} exited {process.ExitCode}: {output}");
    }

    public async Task DisposeAsync()
    {
        if (!ownsDirectory) return;
        if (binaries is not null && (started || File.Exists(Path.Combine(DataDirectory, "postmaster.pid"))))
        {
            // 明確的 -D 只指向此 fixture，絕不指向系統服務或其他叢集。
            // 若停止失敗，寧可保留完整目錄，也不刪除執行中的叢集。
            await RunAsync(binaries.PgCtl, "-D", DataDirectory, "-w", "-t", "60", "-m", "fast", "stop");
            started = false;
        }
        Directory.Delete(root, recursive: true);
        ownsDirectory = false;
    }
}
