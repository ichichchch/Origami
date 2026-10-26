namespace Origami.Web.Services;

public sealed class StoragePaths
{
    public string Root { get; }
    public string Images { get; }
    public string Staging { get; }
    public string Keys { get; }

    public StoragePaths(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Storage:RootPath"];
        // An explicit setting must be absolute. Without one, the default keeps uploads and
        // keys outside the application directory.
        if (string.IsNullOrWhiteSpace(configured)) Root = Path.GetFullPath(DefaultRoot());
        else if (!Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException("持久化儲存必須是絕對路徑。");
        else Root = Path.GetFullPath(configured);
        var applicationRoot = Path.GetFullPath(environment.ContentRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if ((Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(applicationRoot, comparison))
            throw new InvalidOperationException("持久化儲存不可位於應用發布目錄內。");
        Images = Path.Combine(Root, "images");
        Staging = Path.Combine(Root, "staging");
        Keys = Path.Combine(Root, "keys");
        Directory.CreateDirectory(Images);
        Directory.CreateDirectory(Staging);
        Directory.CreateDirectory(Keys);
    }

    private static string DefaultRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new InvalidOperationException("無法判定本機儲存目錄，請以 Storage:RootPath 指定絕對路徑。");
        return Path.Combine(local, "Origami", "storage");
    }
}
