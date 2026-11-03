namespace Origami.Core.Services;

/// <summary>內建目錄：出廠即有的受控包清單，值全為白名單與界限內常數。
/// 安裝時把宣告完整寫入 SitePackage（站點隔離、隨站匯出），目錄後續調整不影響已安裝站台。</summary>
public static class PackageCatalog
{
    public sealed record CatalogEntry(PackageKind Kind, string Slug, string Name, string Description, PackageManifest Manifest);

    public static readonly IReadOnlyList<CatalogEntry> All =
    [
        Theme("ink", "墨韻", "文人長文風：襯線正文與標題、寬鬆行距與深藍基調。",
            "#1f3a5f", 66, true, true, ThemePresets.CardStyleList, "serif", "serif", 2.1, 2.4, 1.05),
        Theme("sunlit", "暖陽", "雜誌感版面：無襯線正文搭配襯線標題與暖橘強調色。",
            "#b3541e", 70, true, true, ThemePresets.CardStyleList, "sans", "serif", 1.9, 1.6, 1.1),
        Theme("archive", "檔案室", "資料感版面：等寬字體、格狀卡片與放大的標題比例。",
            "#4a4a4a", 72, false, true, ThemePresets.CardStyleGrid, "mono", "mono", 1.7, 1.2, 1.25),
        Theme("meadow", "草原", "舒朗的閱讀節奏：寬鬆行距與綠色基調，附少量受控 CSS 示範。",
            "#4f8a3d", 68, true, true, ThemePresets.CardStyleList, "sans", "sans", 2.0, 2.2, 1,
            "a{text-underline-offset:3px}"),
        Widgets("sidebar-starter", "側欄入門包", "側欄的基本組合：搜尋框、最新文章與分類。",
            new ManifestWidget(WidgetArea.Sidebar, WidgetType.Search, "", null, null, null),
            new ManifestWidget(WidgetArea.Sidebar, WidgetType.RecentPosts, "最新文章", 5, null, null),
            new ManifestWidget(WidgetArea.Sidebar, WidgetType.Categories, "分類", null, null, null)),
        Widgets("footer-basics", "頁尾基礎包", "頁尾的基本組合：標籤、最新評論與搜尋框。",
            new ManifestWidget(WidgetArea.Footer, WidgetType.Tags, "標籤", null, null, null),
            new ManifestWidget(WidgetArea.Footer, WidgetType.RecentComments, "最新評論", 5, null, null),
            new ManifestWidget(WidgetArea.Footer, WidgetType.Search, "", null, null, null)),
        Widgets("about-kit", "互動包", "以「關於我」文字與最新評論組成的側欄，頁尾附搜尋框。",
            new ManifestWidget(WidgetArea.Sidebar, WidgetType.Text, "關於我", null,
                "你好，歡迎來到我的網站。\n\n這裡記錄我的想法與作品。", null),
            new ManifestWidget(WidgetArea.Sidebar, WidgetType.RecentComments, "最新評論", 5, null, null),
            new ManifestWidget(WidgetArea.Footer, WidgetType.Search, "", null, null, null))
    ];

    public static CatalogEntry? Find(string slug) =>
        All.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>目錄條目在建構時走同一套驗證；若開發者填入非法值會在首次存取即失敗，屬開發期錯誤。</summary>
    private static CatalogEntry Theme(string slug, string name, string description, string accent, int width,
        bool byline, bool cover, string cardStyle, string fontBody, string fontHeading,
        double proseLine, double listGap, double headingScale, string? css = null) =>
        new(PackageKind.Theme, slug, name, description,
            MarketplaceRules.ValidateManifest(new PackageManifest(slug, name, description, PackageKind.Theme,
                new ThemeManifest(accent, width, byline, cover, cardStyle, fontBody, fontHeading,
                    proseLine, listGap, headingScale, css), null)));

    private static CatalogEntry Widgets(string slug, string name, string description, params ManifestWidget[] widgets) =>
        new(PackageKind.Widgets, slug, name, description,
            MarketplaceRules.ValidateManifest(new PackageManifest(slug, name, description, PackageKind.Widgets,
                null, [.. widgets])));
}
