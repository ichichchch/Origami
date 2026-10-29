namespace Origami.Core.Services;

public sealed class StoragePaths
{
    private readonly SiteContext? siteContext;

    public string Root { get; }
    // scope 可能先建立、稍後才解析站點；不可在建構時快取站點路徑。
    public string SiteRoot
    {
        get
        {
            var siteId = siteContext?.SiteId ?? SiteDefaults.DefaultSiteId;
            if (siteId == Guid.Empty) throw new InvalidOperationException("尚未解析目前站點，無法存取儲存目錄。");
            return siteId == SiteDefaults.DefaultSiteId ? Root : Path.Combine(Root, "sites", siteId.ToString("N"));
        }
    }
    public string Images => EnsureDirectory(Path.Combine(SiteRoot, "images"));
    /// <summary>非圖片媒體（PDF／音訊／影片）專用目錄；與圖片分開存放，供提供時區隔行為。</summary>
    public string Files => EnsureDirectory(Path.Combine(SiteRoot, "files"));
    public string Staging => EnsureDirectory(Path.Combine(SiteRoot, "staging"));
    public string Keys { get; }

    public StoragePaths(IConfiguration configuration, IHostEnvironment environment, SiteContext? siteContext = null)
    {
        this.siteContext = siteContext;
        var configured = configuration["Storage:RootPath"];
        // 明確設定必須是絕對路徑。未設定時，預設值會讓上傳與
        // 金鑰位於應用目錄之外。
        if (string.IsNullOrWhiteSpace(configured)) Root = Path.GetFullPath(DefaultRoot());
        else if (!Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException("持久化儲存必須是絕對路徑。");
        else Root = Path.GetFullPath(configured);
        var applicationRoot = Path.GetFullPath(environment.ContentRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if ((Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(applicationRoot, comparison))
            throw new InvalidOperationException("持久化儲存不可位於應用發布目錄內。");
        Keys = Path.Combine(Root, "keys");
        // 保留舊建構行為；屬性取用時也確保稍後切換的站點目錄存在。
        _ = Images;
        _ = Files;
        _ = Staging;
        Directory.CreateDirectory(Keys);
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string DefaultRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new InvalidOperationException("無法判定本機儲存目錄，請以 Storage:RootPath 指定絕對路徑。");
        return Path.Combine(local, "Origami", "storage");
    }
}
