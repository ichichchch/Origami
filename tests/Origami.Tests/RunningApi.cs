namespace Origami.Tests;

// A real HTTPS Kestrel process for the machine-facing Api host, mirroring RunningSite's isolation:
// a test-only certificate, its own port, and no machine trust-store changes.
internal sealed class RunningApi : IAsyncDisposable
{
    private readonly string connectionString;
    private readonly string root;
    private readonly string certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly X509Certificate2 certificate;
    private Process? process;
    public Uri Address { get; private set; } = null!;

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

    public async Task StartAsync(string environment = "Development")
    {
        if (process is not null) throw new InvalidOperationException("The test API server is already running.");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Address = new Uri($"https://127.0.0.1:{port}");
        var solutionRoot = RunningSite.FindSolutionRoot();
        var project = Path.Combine(solutionRoot, "src", "Origami.Api");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var assembly = Path.Combine(project, "bin", configuration, "net10.0", "Origami.Api.dll");
        if (!File.Exists(assembly)) throw new InvalidOperationException("Build the Api project before API tests.");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = project, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(assembly);
        RunningSite.ConfigureAiEnvironment(start.Environment);
        start.Environment["ASPNETCORE_URLS"] = Address.ToString();
        // DOTNET_ENVIRONMENT takes precedence over ASPNETCORE_ENVIRONMENT in WebApplication.
        start.Environment["DOTNET_ENVIRONMENT"] = environment;
        start.Environment["ConnectionStrings__CMS"] = connectionString;
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = Path.Combine(root, "test.pfx");
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = certificatePassword;
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Capture(object sender, DataReceivedEventArgs e)
        {
            if (e.Data?.Contains("Application started.", StringComparison.Ordinal) == true) ready.TrySetResult();
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

    public HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, received, _, _) => received?.Thumbprint == certificate.Thumbprint
        };
        return new HttpClient(handler) { BaseAddress = Address };
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
