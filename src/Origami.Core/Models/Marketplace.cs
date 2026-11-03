namespace Origami.Core.Models;

/// <summary>受控包種類：主題包（版型宣告）或小工具包（內建型別的預先組合）。</summary>
public enum PackageKind { Theme = 0, Widgets = 1 }

/// <summary>受控包來源：程式內建目錄或後台上傳。</summary>
public enum PackageSource { BuiltIn = 0, Upload = 1 }

/// <summary>
/// 受控包：以宣告式擴充包安裝版型或小工具組合；manifest 一律經驗證後標準化儲存，
/// 不含任何可執行程式碼。渲染端對儲存值重跑驗證，不信任資料庫內容。
/// </summary>
public sealed class SitePackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public PackageKind Kind { get; set; }
    /// <summary>包識別（小寫 slug）；主題包啟用時以 pkg:{slug} 寫入站台設定的版型預設欄位。</summary>
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>驗證後重新序列化的標準 manifest JSON。</summary>
    public string ManifestJson { get; set; } = "{}";
    public PackageSource Source { get; set; }
    /// <summary>主題包：是否為現行版型（設定欄位的鏡像）；小工具包：公開投影過濾開關。</summary>
    public bool IsEnabled { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public CmsSite Site { get; set; } = null!;
}

/// <summary>包 manifest 的強型別形狀；反序列化後由 MarketplaceRules 逐欄位驗證。</summary>
public sealed record PackageManifest(string Slug, string Name, string Description, PackageKind Kind,
    ThemeManifest? Theme, List<ManifestWidget>? Widgets);

/// <summary>主題包的版型宣告；所有欄位值皆須通過白名單／界限驗證。</summary>
public sealed record ThemeManifest(string AccentColor, int ReadingWidth, bool ShowByline, bool ShowCover,
    string CardStyle, string FontBody, string FontHeading, double ProseLine, double ListGap,
    double HeadingScale, string? Css);

/// <summary>小工具包內的單一小工具定義；組態欄位與 WidgetService 的驗證規則一致。</summary>
public sealed record ManifestWidget(WidgetArea Area, WidgetType Type, string? Title, int? Count,
    string? Markdown, string? Url);
