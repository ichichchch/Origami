namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PersistenceTests : ServiceTestBase
{
    private readonly IsolatedPostgres postgres;

    public PersistenceTests(IsolatedPostgres postgres) : base(postgres) => this.postgres = postgres;

    [WindowsPersistenceFact]
    public async Task Restart_and_database_filesystem_restore_preserve_content_images_and_Windows_DPAPI_cookies_on_same_user_and_machine()
    {
        // ProtectKeysWithDpapi 會把金鑰環綁定到目前的 Windows 使用者與機器。
        // 其他平台上的明文金鑰環不算此還原路徑的驗收。
        var binaries = PostgresBinaries.Find()!;
        var binaryDirectory = Path.GetDirectoryName(binaries.PgCtl)!;
        var sourceConnection = new NpgsqlConnectionStringBuilder(App.ConnectionString);
        AssertFixtureDatabase(sourceConnection);
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var source = new RunningSite(App.ConnectionString);
        var storage = CreateStorage(source.StorageRoot);

        MediaItem media;
        ContentEdit post;
        using (var actor = await App.AdministratorAsync())
        {
            var service = new MediaService(App.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
                actor.Services.GetRequiredService<AdminAccess>(), storage, TimeProvider.System,
                NullLogger<MediaService>.Instance);
            using var image = new MemoryStream(CreateImage());
            media = await service.UploadAsync(image, "persistence.png");
            post = NewContent("Persistent published post", status: ContentStatus.Published);
            post.Markdown = "## Persistent body\n\n**Original content** — 重啟與還原後仍相同。\n\n" + media.Markdown;
            post = await actor.Content.SaveAsync(post);
        }

        // MediaService 會重新編碼上傳圖片；基準是發布後的檔案，而非輸入的 PNG。
        var imageBytes = await File.ReadAllBytesAsync(Path.Combine(storage.Images, media.FileName));
        Assert.NotEmpty(imageBytes);
        Assert.Equal(media.Size, imageBytes.LongLength);
        var contentId = post.Id!.Value;
        var databaseBefore = await ReadDatabaseAsync(App.ConnectionString, contentId, media.Id);
        Assert.Equal(post.Title, databaseBefore.Content.Title);
        Assert.Equal(post.Slug, databaseBefore.Content.Slug);
        Assert.Equal(ContentStatus.Published, databaseBefore.Content.Status);
        Assert.Equal(Encoding.UTF8.GetBytes(post.Markdown!), Encoding.UTF8.GetBytes(databaseBefore.Content.Markdown));
        Assert.NotNull(databaseBefore.Content.PublishedAt);

        await source.StartAsync();
        var cookies = new CookieContainer();
        CookieContainer originalCookies;
        byte[] articleBytes;
        using (var client = source.CreateClient(cookies: cookies))
        {
            await AssertAnonymousAsync(client);
            using var login = await HttpSecurityTests.LoginAsync(client, password);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Equal("/admin/posts", new Uri(client.BaseAddress!, login.Headers.Location!).AbsolutePath);
            originalCookies = CopyAuthenticationCookie(cookies, source.Address);
            await AssertAuthenticatedAsync(client, post.Title, media);
            articleBytes = await ReadPublishedArticleAsync(client, post, media);
            await AssertImageAsync(client, media, imageBytes);
        }
        var originalKeys = ReadDpapiKeyFingerprints(storage.Keys);
        using (var visitor = source.CreateClient())
        {
            Assert.Equal(articleBytes, await ReadPublishedArticleAsync(visitor, post, media));
            await AssertImageAsync(visitor, media, imageBytes);
        }

        await source.StopAsync();
        await source.StartAsync();
        // StartAsync 會改用新的 HTTPS 埠。Cookie 以主機為範圍，而這個客戶端
        // 只拿到原始驗證 cookie 的副本，並未重新登入。
        using (var restarted = source.CreateClient(cookies: CopyAuthenticationCookie(originalCookies, source.Address)))
        {
            await AssertAuthenticatedAsync(restarted, post.Title, media);
            Assert.Equal(articleBytes, await ReadPublishedArticleAsync(restarted, post, media));
            await AssertImageAsync(restarted, media, imageBytes);
        }
        Assert.Equal(databaseBefore, await ReadDatabaseAsync(App.ConnectionString, contentId, media.Id));
        Assert.Equal(originalKeys, ReadDpapiKeyFingerprints(storage.Keys));

        // 在資料庫與檔案系統備份前，先讓唯一的網站實例靜止。絕不傾印仍在
        // 寫入的實例，再假設稍後複製的圖片／金鑰是一致的快照。
        await source.StopAsync();
        var backupRoot = Directory.CreateTempSubdirectory("Origami.PersistenceBackup-").FullName;
        try
        {
            var archive = Path.Combine(backupRoot, "database.dump");
            await RunPostgresToolAsync(Path.Combine(binaryDirectory, "pg_dump.exe"), sourceConnection, backupRoot,
                "--format=custom", "--no-owner", "--no-privileges", "--file", archive);
            Assert.True(new FileInfo(archive).Length > 0, "pg_dump must create a nonempty archive.");
            CopyDirectory(storage.Images, Path.Combine(backupRoot, "images"));
            CopyDirectory(storage.Keys, Path.Combine(backupRoot, "keys"));
            Assert.Equal(originalKeys, ReadDpapiKeyFingerprints(Path.Combine(backupRoot, "keys")));

            // 只有此 fixture 能建立／刪除目標資料庫。目標起初只有套用遷移後的
            // 空結構，因此在這裡刻意使用 pg_restore --clean --if-exists。
            var targetConnectionString = await postgres.CreateDatabaseAsync();
            try
            {
                var targetConnection = new NpgsqlConnectionStringBuilder(targetConnectionString);
                AssertFixtureDatabase(targetConnection);
                Assert.Equal(sourceConnection.Host, targetConnection.Host);
                Assert.Equal(sourceConnection.Port, targetConnection.Port);
                Assert.Equal(sourceConnection.Username, targetConnection.Username);
                Assert.NotEqual(sourceConnection.Database, targetConnection.Database);
                await using (var empty = CreateContext(targetConnectionString))
                {
                    Assert.False(await empty.Contents.AnyAsync());
                    Assert.False(await empty.MediaAssets.AnyAsync());
                    Assert.False(await empty.Users.AnyAsync());
                }

                await using var restored = new RunningSite(targetConnectionString);
                Assert.False(string.Equals(Path.GetFullPath(source.StorageRoot), Path.GetFullPath(restored.StorageRoot),
                    StringComparison.OrdinalIgnoreCase), "Restoration must use a different owned storage root.");
                var restoredStorage = CreateStorage(restored.StorageRoot);
                Assert.Empty(Directory.GetFileSystemEntries(restoredStorage.Images));
                Assert.Empty(Directory.GetFileSystemEntries(restoredStorage.Keys));
                await RunPostgresToolAsync(Path.Combine(binaryDirectory, "pg_restore.exe"), targetConnection, backupRoot,
                    "--clean", "--if-exists", "--exit-on-error", "--single-transaction", "--no-owner", "--no-privileges", archive);
                CopyDirectory(Path.Combine(backupRoot, "images"), restoredStorage.Images);
                CopyDirectory(Path.Combine(backupRoot, "keys"), restoredStorage.Keys);
                Assert.Equal(originalKeys, ReadDpapiKeyFingerprints(restoredStorage.Keys));
                Assert.Equal(databaseBefore, await ReadDatabaseAsync(targetConnectionString, contentId, media.Id));
                Assert.Equal(imageBytes, await File.ReadAllBytesAsync(Path.Combine(restoredStorage.Images, media.FileName)));

                await restored.StartAsync();
                // 還要證明複製過去的 DPAPI 加密金鑰確實可用，而不只是存在：
                // 原始來源發出的 cookie 必須能在這裡解密。
                using (var previousSession = restored.CreateClient(cookies: CopyAuthenticationCookie(originalCookies, restored.Address)))
                    await AssertAuthenticatedAsync(previousSession, post.Title, media);
                using (var client = restored.CreateClient())
                {
                    await AssertAnonymousAsync(client);
                    Assert.Equal(articleBytes, await ReadPublishedArticleAsync(client, post, media));
                    await AssertImageAsync(client, media, imageBytes);
                    using var login = await HttpSecurityTests.LoginAsync(client, password);
                    Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
                    Assert.Equal("/admin/posts", new Uri(client.BaseAddress!, login.Headers.Location!).AbsolutePath);
                    await AssertAuthenticatedAsync(client, post.Title, media);
                    Assert.Equal(articleBytes, await ReadPublishedArticleAsync(client, post, media));
                    await AssertImageAsync(client, media, imageBytes);
                }
                Assert.Equal(originalKeys, ReadDpapiKeyFingerprints(restoredStorage.Keys));
                await restored.StopAsync();
            }
            finally
            {
                // RunningSite 在此區塊之前已釋放，目標連線皆已關閉。
                await postgres.DropDatabaseAsync(targetConnectionString);
            }
        }
        finally
        {
            // 來源資料庫／網站屬於 ServiceTestBase／RunningSite，不由此清理負責。
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    private static async Task AssertAnonymousAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/admin/posts");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    private static async Task AssertAuthenticatedAsync(HttpClient client, string title, MediaItem media)
    {
        using var posts = await client.GetAsync("/admin/posts");
        Assert.Equal(HttpStatusCode.OK, posts.StatusCode);
        Assert.Contains(title, await posts.Content.ReadAsStringAsync());
        using var images = await client.GetAsync("/admin/media");
        Assert.Equal(HttpStatusCode.OK, images.StatusCode);
        var html = await images.Content.ReadAsStringAsync();
        Assert.Contains(media.OriginalName, html);
        Assert.Contains(media.Url, html);
    }

    private static async Task<byte[]> ReadPublishedArticleAsync(HttpClient client, ContentEdit post, MediaItem media)
    {
        using var response = await client.GetAsync("/posts/" + Uri.EscapeDataString(post.Slug!));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var document = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
        var article = document.QuerySelector("article.reading-column");
        Assert.NotNull(article);
        Assert.Equal(post.Title, article.QuerySelector("h1")?.TextContent);
        Assert.Equal("Original content", article.QuerySelector("strong")?.TextContent);
        Assert.Equal(media.Url, article.QuerySelector("img")?.GetAttribute("src"));
        // 比較整篇渲染後的文章位元組，排除文件中每次請求都不同的框架
        // 狀態／防偽 token；原始 Markdown 也已在 PostgreSQL 端比對。
        return Encoding.UTF8.GetBytes(article.OuterHtml);
    }

    private static async Task AssertImageAsync(HttpClient client, MediaItem media, byte[] expected)
    {
        using var response = await client.GetAsync(media.Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(media.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
    }

    private static CookieContainer CopyAuthenticationCookie(CookieContainer source, Uri address)
    {
        var cookie = source.GetCookies(address)["__Host-Origami.Admin"];
        Assert.True(cookie is not null, "HTTP login must issue the administrator authentication cookie.");
        var result = new CookieContainer();
        result.Add(address, new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path)
        {
            Secure = cookie.Secure, HttpOnly = cookie.HttpOnly
        });
        return result;
    }

    private static StoragePaths CreateStorage(string root) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:RootPath"] = root }).Build(),
        new TestHostEnvironment());

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Origami.Tests";
        // 這裡不建立任何目錄；這個既有的測試輸出根目錄位於 RunningSite 暫存儲存之外。
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static byte[] CreateImage()
    {
        using var bitmap = new SKBitmap(32, 24);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Teal);
        using var paint = new SKPaint { Color = SKColors.Gold };
        canvas.DrawRect(8, 6, 16, 12, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed record ContentSnapshot(Guid Id, ContentKind Kind, string Title, string Slug, string Markdown,
        ContentStatus Status, Guid Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt);
    private sealed record DatabaseSnapshot(ContentSnapshot Content, MediaItem Media);

    private static CmsDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<CmsDbContext>().UseNpgsql(connectionString, o => o.UseVector()).Options);

    private static async Task<DatabaseSnapshot> ReadDatabaseAsync(string connectionString, Guid contentId, Guid mediaId)
    {
        await using var db = CreateContext(connectionString);
        var content = await db.Contents.AsNoTracking().Where(x => x.Id == contentId)
            .Select(x => new ContentSnapshot(x.Id, x.Kind, x.Title, x.Slug, x.Markdown, x.Status, x.Version,
                x.CreatedAt, x.UpdatedAt, x.PublishedAt)).SingleAsync();
        var media = await db.MediaAssets.AsNoTracking().Where(x => x.Id == mediaId)
            .Select(x => new MediaItem(x.Id, x.FileName, x.OriginalName, x.ContentType, x.Size, x.Width, x.Height, x.UploadedAt)).SingleAsync();
        return new(content, media);
    }

    private static (string Name, string Hash)[] ReadDpapiKeyFingerprints(string directory)
    {
        var files = Directory.GetFiles(directory, "key-*.xml").Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(files);
        return files.Select(file =>
        {
            var bytes = File.ReadAllBytes(file);
            using var stream = new MemoryStream(bytes);
            var document = XDocument.Load(stream);
            Assert.True(document.Descendants().Any(x => x.Name.LocalName == "encryptedSecret"
                && ((string?)x.Attribute("decryptorType"))?.StartsWith(
                    "Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlDecryptor,", StringComparison.Ordinal) == true),
                "Persisted Windows Data Protection keys must be encrypted with DPAPI.");
            Assert.False(document.Descendants().Any(x => x.Name.LocalName == "masterKey"),
                "A plaintext master key is not Windows DPAPI persistence acceptance.");
            // 比較指紋，而不是在斷言失敗時印出金鑰檔內容。
            return (Path.GetFileName(file), Convert.ToHexString(SHA256.HashData(bytes)));
        }).ToArray();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static void AssertFixtureDatabase(NpgsqlConnectionStringBuilder connection)
    {
        Assert.Equal("127.0.0.1", connection.Host);
        Assert.InRange(connection.Port, 1, 65535);
        Assert.NotEqual(5432, connection.Port);
        Assert.Equal("postgres", connection.Username);
        Assert.Matches("^origami_test_[0-9a-f]{32}$", connection.Database ?? "");
        Assert.True(!string.IsNullOrEmpty(connection.Password), "The isolated fixture must supply a password.");
    }

    private static async Task RunPostgresToolAsync(string executable, NpgsqlConnectionStringBuilder connection,
        string workingDirectory, params string[] arguments)
    {
        AssertFixtureDatabase(connection);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = workingDirectory
        };
        foreach (var key in start.Environment.Keys.Where(x => x.StartsWith("PG", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["PGPASSWORD"] = connection.Password;
        foreach (var argument in new[] { "--host", connection.Host!, "--port", connection.Port.ToString(CultureInfo.InvariantCulture),
            "--dbname", connection.Database!, "--username", connection.Username!, "--no-password" }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Could not start the PostgreSQL backup/restore tool.");
        // 持續排空兩條輸出管線，絕不把工具輸出、連線字串或認證資訊寫進記錄。
        var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            throw new TimeoutException(Path.GetFileName(executable) + " exceeded the isolated backup/restore timeout.");
        }
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(executable)} exited {process.ExitCode}; tool output is withheld to protect credentials.");
    }
}

public sealed class WindowsPersistenceFactAttribute : FactAttribute
{
    public WindowsPersistenceFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows DPAPI persistence not executed: recovery requires the same Windows user and machine; plaintext non-Windows keys are not equivalent acceptance.";
        else if (PostgresBinaries.Find() is not { } binaries)
            Skip = PostgresBinaries.MissingDiagnostic;
        else if (!File.Exists(Path.Combine(Path.GetDirectoryName(binaries.PgCtl)!, "pg_dump.exe"))
            || !File.Exists(Path.Combine(Path.GetDirectoryName(binaries.PgCtl)!, "pg_restore.exe")))
            Skip = "PostgreSQL persistence not executed: pg_dump.exe and pg_restore.exe must exist next to the fixture's pg_ctl.exe; no tools are installed by this test.";
    }
}
