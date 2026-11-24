namespace Origami.Tests;

public sealed class WebDavBackupTests
{
    private static WebDavBackupUploader Uploader(HttpMessageHandler handler, Guid? siteId = null,
        ILogger<WebDavBackupUploader>? logger = null)
    {
        var site = new SiteContext { SiteId = siteId ?? SiteDefaults.DefaultSiteId };
        return new(new HttpClient(handler), site, logger ?? NullLogger<WebDavBackupUploader>.Instance);
    }

    [Fact]
    public async Task Upload_uses_basic_auth_site_isolation_and_remote_retention()
    {
        var requests = new List<(string Method, Uri Url, string? Authorization)>();
        var trimErrors = new List<string>();
        byte[]? uploaded = null;
        var temporaryDirectory = Directory.CreateTempSubdirectory("origami-webdav-");
        var fileName = "origami-backup-20261124-120000.zip";
        var local = Path.Combine(temporaryDirectory.FullName, fileName);
        File.WriteAllBytes(local, "backup-bytes"u8.ToArray());
        var directory = "/backups/backups/site-" + SiteDefaults.DefaultSiteId.ToString("N") + "/";
        var current = directory + fileName;
        var old = directory + "origami-backup-20200101-000000.zip";
        try
        {
            var handler = new StubHandler(async (request, ct) =>
            {
                requests.Add((request.Method.Method, request.RequestUri!, request.Headers.Authorization?.ToString()));
                if (request.Method == HttpMethod.Put)
                {
                    uploaded = await request.Content!.ReadAsByteArrayAsync(ct);
                    return new HttpResponseMessage(HttpStatusCode.Created);
                }
                if (request.Method.Method == "PROPFIND")
                {
                    var xml = "<d:multistatus xmlns:d=\"DAV:\">" +
                        $"<d:response><d:href>{current}</d:href><d:propstat><d:prop><d:resourcetype /></d:prop></d:propstat></d:response>" +
                        $"<d:response><d:href>{directory}origami-backup-20250101-000000.zip</d:href><d:propstat><d:prop><d:resourcetype /></d:prop></d:propstat></d:response>" +
                        $"<d:response><d:href>{old}</d:href><d:propstat><d:prop><d:resourcetype /></d:prop></d:propstat></d:response>" +
                        $"<d:response><d:href>{directory}notes.txt</d:href><d:propstat><d:prop><d:resourcetype /></d:prop></d:propstat></d:response>" +
                        "<d:response><d:href>/backups/site-other/origami-backup-20190101-000000.zip</d:href><d:propstat><d:prop><d:resourcetype /></d:prop></d:propstat></d:response>" +
                        "<d:response><d:href>/backups/</d:href><d:propstat><d:prop><d:resourcetype><d:collection /></d:resourcetype></d:prop></d:propstat></d:response>" +
                        "</d:multistatus>";
                    return new HttpResponseMessage(HttpStatusCode.MultiStatus)
                    {
                        Content = new StringContent(xml, Encoding.UTF8, "application/xml")
                    };
                }
                if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
                return new HttpResponseMessage(HttpStatusCode.Created);
            });
            var options = new WebDavOptions
            {
                Url = "https://dav.example.test/backups", Username = "backup-user", Password = "secret", Directory = "backups"
            };

            await Uploader(handler, logger: new CaptureLogger(trimErrors)).UploadAsync(options, local, 2);

            Assert.True(trimErrors.Count == 0, string.Join(Environment.NewLine, trimErrors));
            Assert.Equal("backup-bytes"u8.ToArray(), uploaded);
            Assert.Equal(new[] { "MKCOL", "MKCOL", "MKCOL", "PUT", "PROPFIND", "DELETE" },
                requests.Select(x => x.Method).ToArray());
            Assert.Equal(new Uri("https://dav.example.test/backups/"), requests[0].Url);
            Assert.Equal(new Uri("https://dav.example.test" + current), requests[3].Url);
            Assert.Equal(new Uri("https://dav.example.test" + old), requests[5].Url);
            Assert.All(requests, request => Assert.StartsWith("Basic ", request.Authorization));
            Assert.DoesNotContain(requests, request => request.Url.Host != "dav.example.test");
        }
        finally { Directory.Delete(temporaryDirectory.FullName, recursive: true); }
    }

    [Theory]
    [InlineData("http://dav.example.test/backups")]
    [InlineData("https://user:password@dav.example.test/backups")]
    [InlineData("https://dav.example.test/backups?token=secret")]
    [InlineData("https://dav.example.test/backups#fragment")]
    [InlineData("https://127.0.0.1/backups")]
    [InlineData("https://10.0.0.1/backups")]
    [InlineData("https://169.254.169.254/backups")]
    [InlineData("https://localhost/backups")]
    [InlineData("https://[::1]/backups")]
    public async Task Insecure_or_ambiguous_destinations_are_refused(string url)
    {
        var local = Path.GetTempFileName();
        try
        {
            var error = await Assert.ThrowsAsync<CmsException>(() => Uploader(new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)))).UploadAsync(
                new WebDavOptions { Url = url, Username = "u", Password = "p" }, local, 1));
            Assert.Contains("HTTPS", error.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(local); }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/./b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    public async Task Unsafe_remote_directories_are_refused(string directory)
    {
        var local = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<CmsException>(() => Uploader(new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)))).UploadAsync(
                new WebDavOptions { Url = "https://dav.example.test/", Username = "u", Password = "p", Directory = directory },
                local, 1));
        }
        finally { File.Delete(local); }
    }

    [Fact]
    public async Task Redirects_and_missing_credentials_are_not_followed_or_silently_ignored()
    {
        var local = Path.GetTempFileName();
        try
        {
            var methods = new List<string>();
            var uploader = Uploader(new StubHandler(async (request, _) =>
            {
                methods.Add(request.Method.Method);
                return request.Method == HttpMethod.Put
                    ? new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
                    {
                        Headers = { Location = new Uri("https://elsewhere.example.test/upload") }
                    }
                    : new HttpResponseMessage(HttpStatusCode.Created);
            }));
            await Assert.ThrowsAsync<CmsException>(() => uploader.UploadAsync(
                new WebDavOptions { Url = "https://dav.example.test/", Username = "u", Password = "p" }, local, 1));
            Assert.DoesNotContain("PROPFIND", methods);

            methods.Clear();
            await Assert.ThrowsAsync<CmsException>(() => uploader.UploadAsync(
                new WebDavOptions { Url = "https://dav.example.test/", Username = "u" }, local, 1));
            Assert.Empty(methods);
        }
        finally { File.Delete(local); }
    }

    private sealed class CaptureLogger(List<string> errors) : ILogger<WebDavBackupUploader>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) errors.Add(formatter(state, exception));
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
