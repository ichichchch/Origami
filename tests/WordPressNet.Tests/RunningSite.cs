using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WordPressNet.Tests;

// A real HTTPS Kestrel process, with a test-only certificate and no machine trust-store changes.
internal sealed class RunningSite : IAsyncDisposable
{
    private readonly string connectionString;
    private readonly string root;
    private readonly string certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly X509Certificate2 certificate;
    private Process? process;
    public Uri Address { get; private set; } = null!;
    public string StorageRoot => Path.Combine(root, "storage");

    public RunningSite(string connectionString)
    {
        this.connectionString = connectionString;
        root = Path.Combine(Path.GetTempPath(), "wordpressnet-site-" + Guid.NewGuid().ToString("N"));
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

    public async Task StartAsync(bool reuseAddress = false)
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
        var solutionRoot = FindSolutionRoot();
        var project = Path.Combine(solutionRoot, "src", "WordPressNet.Web");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var assembly = Path.Combine(project, "bin", configuration, "net10.0", "WordPressNet.Web.dll");
        if (!File.Exists(assembly)) throw new InvalidOperationException("Build the Web project before browser tests.");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = project, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(assembly);
        start.Environment["ASPNETCORE_URLS"] = Address.ToString();
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__Cms"] = connectionString;
        start.Environment["Storage__RootPath"] = StorageRoot;
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = Path.Combine(root, "test.pfx");
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = certificatePassword;
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.Contains("Now listening on:", StringComparison.Ordinal) == true) ready.TrySetResult();
        };
        process.ErrorDataReceived += (_, _) => { };
        process.Exited += (_, _) => ready.TrySetException(new InvalidOperationException("The isolated test site exited before becoming ready."));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(45));
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
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        process.Dispose();
        process = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        certificate.Dispose();
        Directory.Delete(root, recursive: true);
    }

    internal static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "WordPressNet.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate the WordPressNet solution.");
    }
}
