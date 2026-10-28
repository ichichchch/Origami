namespace Origami.Tests;

// 機器介面 Api 主機的真實 HTTPS Kestrel 處理程序，隔離方式比照 RunningSite：
// 僅供測試的憑證、專屬連接埠，且不變更機器的信任存放區。
internal sealed class RunningApi : IAsyncDisposable
{
    private readonly string connectionString;
    private readonly string root;
    private readonly string certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly X509Certificate2 certificate;
    private readonly ConcurrentQueue<string> consoleErrors = new();
    private Process? process;
    public Uri Address { get; private set; } = null!;

    /// <summary>截斷後的伺服器端失敗輸出行，讓 500 錯誤的根因在測試輸出中看得到。</summary>
    public IReadOnlyList<string> ConsoleErrors => [.. consoleErrors];

    public RunningApi(string connectionString)
    {
        this.connectionString = connectionString;
        root = Path.Combine(Path.GetTempPath(), "origami-api-" + Guid.NewGuid().ToString("N"));
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

    public async Task StartAsync(string environment = "Development", string? serviceToken = null, bool listenOnHttp = false,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null, string? storageRoot = null)
    {
        if (process is not null) throw new InvalidOperationException("The test API server is already running.");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        // 網站主機以 BFF 身份呼叫此 Api；測試用 HTTP 免去自簽憑證信任問題（僅本機回環）。
        Address = new Uri($"{(listenOnHttp ? "http" : "https")}://127.0.0.1:{port}");
        var solutionRoot = RunningSite.FindSolutionRoot();
        var project = Path.Combine(solutionRoot, "src", "Origami.Api");
        // 優先載入與測試組件同目錄的主機輸出：隔離輸出目錄建置時，bin 配置
        // 路徑推斷會失效，而且測試輸出內的複本即為本次組建的新程式。
        var assembly = Path.Combine(AppContext.BaseDirectory, "Origami.Api.dll");
        if (!File.Exists(assembly))
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            assembly = Path.Combine(project, "bin", configuration, "net10.0", "Origami.Api.dll");
        }
        if (!File.Exists(assembly)) throw new InvalidOperationException("Build the Api project before API tests.");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = project, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(assembly);
        // Ai 覆寫（如測試的 loopback 嵌入供應商）必須與網站主機一致：BFF 之後嵌入與
        // 搜尋都在 Api 主機執行，漏轉發會使語意部分靜默降級為純關鍵字。
        RunningSite.ConfigureAiEnvironment(start.Environment, configurationOverrides);
        start.Environment["ASPNETCORE_URLS"] = Address.ToString();
        // 在 WebApplication 中，DOTNET_ENVIRONMENT 的優先順序高於 ASPNETCORE_ENVIRONMENT。
        start.Environment["DOTNET_ENVIRONMENT"] = environment;
        start.Environment["ConnectionStrings__CMS"] = connectionString;
        // 空值也明確覆寫，避免繼承開發機的 BFF 通行證。
        RunningSite.SetEnvironment(start.Environment, "Api__ServiceToken", serviceToken ?? "");
        // 媒體端點被測試時，Api 主機會寫入上傳的圖片；與網站主機共用同一個儲存根，
        // 網站的 /media 才看得到 Api 寫入的檔案（正式環境兩主機本就指向同一路徑）。
        start.Environment["Storage__RootPath"] = storageRoot ?? Path.Combine(root, "storage");
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = Path.Combine(root, "test.pfx");
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = certificatePassword;
        if (listenOnHttp)
        {
            // HTTP 模式不註冊憑證設定，Kestrel 直接以 http 監聽。
            start.Environment.Remove("ASPNETCORE_Kestrel__Certificates__Default__Path");
            start.Environment.Remove("ASPNETCORE_Kestrel__Certificates__Default__Password");
        }
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorLinesRemaining = 0;
        string Truncate(string line) => line.Length > 200 ? line[..200] : line;
        void Capture(object sender, DataReceivedEventArgs e)
        {
            var line = e.Data;
            if (line is null) return;
            // 徹底截斷：錯誤內容可帶例外訊息，但絕不包含整份堆疊或認證資訊。
            if (line.StartsWith("fail: ", StringComparison.Ordinal))
            {
                consoleErrors.Enqueue(Truncate(line));
                errorLinesRemaining = 6;
            }
            else if (errorLinesRemaining > 0 && line.Trim().Length > 0)
            {
                consoleErrors.Enqueue(Truncate(line));
                errorLinesRemaining--;
            }
            if (line.Contains("Application started.", StringComparison.Ordinal) == true) ready.TrySetResult();
        }
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += Capture;
        process.ErrorDataReceived += Capture;
        process.Exited += (_, _) => ready.TrySetException(new InvalidOperationException("The isolated test API exited before becoming ready."));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    public HttpClient CreateClient(Guid? siteId = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, received, _, _) => received?.Thumbprint == certificate.Thumbprint
        };
        var client = new HttpClient(handler) { BaseAddress = Address };
        if (siteId is { } id) client.DefaultRequestHeaders.Add(AccessClaims.SiteHeader, id.ToString("D"));
        return client;
    }

    public HttpClient CreateBffClient(string serviceToken, string userId, string stamp, Guid? siteId = null)
    {
        var client = CreateClient(siteId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        client.DefaultRequestHeaders.Add(AccessClaims.UserHeader, userId);
        client.DefaultRequestHeaders.Add(AccessClaims.StampHeader, stamp);
        return client;
    }

    public HttpClient CreateCredentialClient(string token, Guid siteId)
    {
        var client = CreateClient(siteId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task StopAsync()
    {
        if (process is null) return;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        process.Dispose();
        process = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        certificate.Dispose();
        Directory.Delete(root, recursive: true);
    }
}
