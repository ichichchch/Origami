namespace Origami.Core.Services;

/// <summary>把完整備份 PUT 到 WebDAV，並在該站點專用目錄保留最新幾份；不跟隨轉址。</summary>
public sealed partial class WebDavBackupUploader(HttpClient http, SiteContext site, ILogger<WebDavBackupUploader> logger)
{
    private const int PropFindMaxBytes = 1024 * 1024;

    public async Task UploadAsync(WebDavOptions options, string localPath, int keep, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Url)) return;
        var directories = ResolveDirectories(options);
        var directory = directories[^1];
        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
            throw new CmsException("WebDAV 備份需要使用者名稱與密碼。");

        await using var file = File.OpenRead(localPath);
        foreach (var current in directories)
            await EnsureDirectoryAsync(current, options, ct);

        var target = new Uri(directory, Uri.EscapeDataString(Path.GetFileName(localPath)));
        using var content = new StreamContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var upload = Request(HttpMethod.Put, target, options);
        upload.Content = content;
        using var response = await http.SendAsync(upload, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new CmsException($"WebDAV 備份上傳失敗（HTTP {(int)response.StatusCode}）。");
        Log.Uploaded(logger, file.Length);

        try { await TrimRemoteAsync(directory, options, Math.Max(1, keep), ct); }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        { Log.TrimFailed(logger, error.GetType().Name); }
    }

    private IReadOnlyList<Uri> ResolveDirectories(WebDavOptions options)
    {
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var root) || root.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(root.Host) || !string.IsNullOrEmpty(root.UserInfo) ||
            !string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment) ||
            !PublicHttpUrlPolicy.IsAllowed(root))
            throw new CmsException("WebDAV 備份網址必須是沒有帳密、查詢參數或片段且可公開連線的 HTTPS 絕對網址。");
        root = new Uri(root.AbsoluteUri.TrimEnd('/') + "/");
        var rawSegments = string.IsNullOrWhiteSpace(options.Directory)
            ? [] : options.Directory.Trim('/').Split('/');
        if (rawSegments.Length > 16)
            throw new CmsException("WebDAV 備份目錄層級過多。");
        foreach (var segment in rawSegments)
        {
            if (segment.Length is 0 or > 80 || segment.Any(char.IsControl) ||
                segment is "." or ".." || segment.Contains('\\') || segment.Contains(':'))
                throw new CmsException("WebDAV 備份目錄含有不安全的路徑區段。");
        }
        var directories = new List<Uri> { root };
        var current = root;
        foreach (var segment in rawSegments.Append("site-" + site.SiteId.ToString("N")))
        {
            current = new Uri(current, Uri.EscapeDataString(segment) + "/");
            directories.Add(current);
        }
        return directories;
    }

    private async Task EnsureDirectoryAsync(Uri directory, WebDavOptions options, CancellationToken ct)
    {
        using var request = Request(new HttpMethod("MKCOL"), directory, options);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed) return;
        throw new CmsException($"WebDAV 備份目錄建立失敗（HTTP {(int)response.StatusCode}）。");
    }

    private async Task TrimRemoteAsync(Uri directory, WebDavOptions options, int keep, CancellationToken ct)
    {
        const string body = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype /></d:prop></d:propfind>";
        using var content = new StringContent(body, Encoding.UTF8, "application/xml");
        using var request = Request(new HttpMethod("PROPFIND"), directory, options);
        request.Content = content;
        request.Headers.Add("Depth", "1");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.MultiStatus)
            throw new CmsException($"WebDAV 備份清單讀取失敗（HTTP {(int)response.StatusCode}）。");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        await ArchiveGuard.CopyBoundedAsync(stream, buffer, 0, PropFindMaxBytes,
            "WebDAV 備份清單回應過大。", ct);
        buffer.Position = 0;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            Async = true
        };
        using var reader = XmlReader.Create(buffer, settings);
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, ct);
        var prefix = directory.AbsolutePath;
        var backups = new List<(string Name, Uri Url)>();
        foreach (var item in document.Descendants().Where(x => x.Name.LocalName == "response"))
        {
            var href = item.Elements().SingleOrDefault(x => x.Name.LocalName == "href")?.Value;
            var type = item.Descendants().FirstOrDefault(x => x.Name.LocalName == "resourcetype");
            if (href is null || type?.Elements().Any(x => x.Name.LocalName == "collection") == true) continue;
            if (!Uri.TryCreate(directory, href, out var url) || url.Scheme != directory.Scheme ||
                url.Host != directory.Host || url.Port != directory.Port ||
                !url.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var name = Path.GetFileName(Uri.UnescapeDataString(url.AbsolutePath.TrimEnd('/')));
            if (BackupName().IsMatch(name)) backups.Add((name, url));
        }
        foreach (var stale in backups.OrderByDescending(x => x.Name, StringComparer.Ordinal).Skip(keep))
        {
            using var delete = Request(HttpMethod.Delete, stale.Url, options);
            using var deleted = await http.SendAsync(delete, HttpCompletionOption.ResponseHeadersRead, ct);
            if (deleted.IsSuccessStatusCode || deleted.StatusCode == HttpStatusCode.NotFound) continue;
            throw new CmsException($"WebDAV 逾期備份刪除失敗（HTTP {(int)deleted.StatusCode}）。");
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri url, WebDavOptions options)
    {
        var request = new HttpRequestMessage(method, url);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Username + ":" + options.Password));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        return request;
    }

    [GeneratedRegex(@"^origami-backup-[0-9]{8}-[0-9]{6}\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupName();

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Information, "WebDAV 異地備份已上傳（{Bytes} 位元組）。")]
        public static partial void Uploaded(ILogger logger, long bytes);

        [LoggerMessage(LogLevel.Warning, "WebDAV 逾期備份清理失敗（{ErrorType}），本機備份不受影響。")]
        public static partial void TrimFailed(ILogger logger, string errorType);
    }
}
