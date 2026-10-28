namespace Origami.Tests;

// 真實的 HTTPS Kestrel 處理程序，使用僅供測試的憑證，且不變更機器的信任存放區。
internal sealed class RunningSite : IAsyncDisposable
{
    private readonly string connectionString;
    private readonly string root;
    private readonly string certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly X509Certificate2 certificate;
    private readonly ConcurrentQueue<string> warningCategories = new();
    private readonly string apiToken = "site-test-token-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private Process? process;
    private RunningApi? api;
    public Uri Address { get; private set; } = null!;
    public Uri? HttpAddress { get; private set; }
    public IReadOnlyCollection<string> WarningCategories => warningCategories.ToArray();
    public string StorageRoot => Path.Combine(root, "storage");

    public RunningSite(string connectionString)
    {
        this.connectionString = connectionString;
        root = Path.Combine(Path.GetTempPath(), "origami-site-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(Path.Combine(root, "test.pfx"), certificate.Export(X509ContentType.Pfx, certificatePassword));
    }

    public async Task StartAsync(bool reuseAddress = false, string environment = "Development",
        IReadOnlyDictionary<string, string?>? configurationOverrides = null, bool listenOnHttp = false)
    {
        if (process is not null) throw new InvalidOperationException("The test server is already running.");
        if (!reuseAddress || Address is null)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            Address = new Uri($"https://127.0.0.1:{port}");
        }
        if (!listenOnHttp) HttpAddress = null;
        else if (!reuseAddress || HttpAddress is null)
        {
            do
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                HttpAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            } while (HttpAddress.Port == Address.Port);
        }
        var solutionRoot = FindSolutionRoot();
        var project = Path.Combine(solutionRoot, "src", "Origami.Web");
        // 優先載入與測試組件同目錄的主機輸出：隔離輸出目錄建置時，bin 配置
        // 路徑推斷會失效，而且測試輸出內的複本即為本次組建的新程式。
        var assembly = Path.Combine(AppContext.BaseDirectory, "Origami.Web.dll");
        if (!File.Exists(assembly))
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            assembly = Path.Combine(project, "bin", configuration, "net10.0", "Origami.Web.dll");
        }
        if (!File.Exists(assembly)) throw new InvalidOperationException("Build the Web project before browser tests.");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = project, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        // 頁面資料一律經 Api 主機（BFF）：先以同一資料庫與服務通行證啟動它，再讓網站指向。
        // Ai 覆寫與儲存根也一併共用：嵌入／搜尋在 Api 執行，媒體由 Api 寫入、網站提供服務。
        api = new RunningApi(connectionString);
        await api.StartAsync(environment: environment, serviceToken: apiToken, listenOnHttp: true,
            configurationOverrides: configurationOverrides, storageRoot: StorageRoot);
        start.ArgumentList.Add(assembly);
        ConfigureAiEnvironment(start.Environment, configurationOverrides);
        SetEnvironment(start.Environment, "Api__BaseUrl", api.Address.ToString());
        SetEnvironment(start.Environment, "Api__ServiceToken", apiToken);
        SetEnvironment(start.Environment, "ASPNETCORE_URLS", HttpAddress is null ? Address.ToString() : $"{Address};{HttpAddress}");
        // 在 WebApplication 中，DOTNET_ENVIRONMENT 的優先順序高於 ASPNETCORE_ENVIRONMENT。
        SetEnvironment(start.Environment, "DOTNET_ENVIRONMENT", environment);
        SetEnvironment(start.Environment, "ConnectionStrings__CMS", connectionString);
        SetEnvironment(start.Environment, "Storage__RootPath", StorageRoot);
        SetEnvironment(start.Environment, "ASPNETCORE_Kestrel__Certificates__Default__Path", Path.Combine(root, "test.pfx"));
        SetEnvironment(start.Environment, "ASPNETCORE_Kestrel__Certificates__Default__Password", certificatePassword);
        SetEnvironment(start.Environment, "Logging__LogLevel__Default", "Warning");
        SetEnvironment(start.Environment, "Logging__LogLevel__Microsoft.Hosting.Lifetime", "Information");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Capture(object sender, DataReceivedEventArgs e)
        {
            var line = e.Data;
            if (line?.StartsWith("warn: ", StringComparison.Ordinal) == true)
            {
                // 只保留分類／事件 ID，絕不保留訊息本文、認證資訊或連線字串。
                var end = line.IndexOf(']');
                if (end >= 0) warningCategories.Enqueue(line[..(end + 1)]);
            }
            // 同時啟用 HTTP 與 HTTPS 時，第一條監聽訊息出現得太早，不能當成就緒。
            if (line?.Contains("Application started.", StringComparison.Ordinal) == true) ready.TrySetResult();
        }
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += Capture;
        process.ErrorDataReceived += Capture;
        process.Exited += (_, _) => ready.TrySetException(new InvalidOperationException("The isolated test site exited before becoming ready."));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    internal static void ConfigureAiEnvironment(IDictionary<string, string?> environment,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        // 先移除各種拼法再套預設值：Linux 可能繼承只有大小寫不同的重複索引鍵，
        // ASP.NET 組態之後會把它們視為同一個索引鍵。
        foreach (var key in environment.Keys.Where(key =>
            ConfigurationKey(key).StartsWith("Ai:", StringComparison.OrdinalIgnoreCase)).ToArray())
            environment.Remove(key);
        foreach (var model in new[] { "Embedding", "Assistant" })
        {
            SetEnvironment(environment, $"Ai__{model}__Endpoint", "http://127.0.0.1:1/disabled");
            SetEnvironment(environment, $"Ai__{model}__Model", "isolated-test-model");
            SetEnvironment(environment, $"Ai__{model}__ApiKey", "");
        }
        if (overrides is not null)
            foreach (var pair in overrides) SetEnvironment(environment, pair.Key, pair.Value);
    }

    internal static void SetEnvironment(IDictionary<string, string?> environment, string key, string? value)
    {
        foreach (var inherited in environment.Keys.Where(candidate =>
            string.Equals(ConfigurationKey(candidate), ConfigurationKey(key), StringComparison.OrdinalIgnoreCase)).ToArray())
            environment.Remove(inherited);
        environment[key] = value;
    }

    private static string ConfigurationKey(string key)
    {
        foreach (var prefix in new[] { "ASPNETCORE_", "DOTNET_" })
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                key = key[prefix.Length..];
                break;
            }
        return key.Replace("__", ":", StringComparison.Ordinal);
    }

    public HttpClient CreateClient(bool allowRedirects = false, CookieContainer? cookies = null)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = allowRedirects, CookieContainer = cookies ?? new CookieContainer(),
            ServerCertificateCustomValidationCallback = (_, received, _, _) => received?.Thumbprint == certificate.Thumbprint
        };
        return new HttpClient(handler) { BaseAddress = Address };
    }

    public async Task StopAsync()
    {
        if (process is null) return;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        // 即使處理程序已退出，仍先排空重導的輸出，再檢查警告。
        await process.WaitForExitAsync();
        process.Dispose();
        process = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (api is not null)
        {
            await api.DisposeAsync();
            api = null;
        }
        certificate.Dispose();
        Directory.Delete(root, recursive: true);
    }

    internal static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Origami.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate the Origami solution.");
    }
}
