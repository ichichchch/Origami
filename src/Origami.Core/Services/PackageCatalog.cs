namespace Origami.Core.Services;

/// <summary>內建目錄：出廠即有的受控包清單，值全為白名單與界限內常數。
/// 安裝時把宣告完整寫入 SitePackage（站點隔離、隨站匯出），目錄後續調整不影響已安裝站台。</summary>
public static class PackageCatalog
{
    public sealed record CatalogEntry(PackageKind Kind, string Slug, string Name, string Description, PackageManifest Manifest);

    public static readonly IReadOnlyList<CatalogEntry> All =
    [
        Theme("inkwell", "墨井", "文人書卷氣：襯線正文與標題、緊湊欄寬與寬鬆行距；引文帶強調色豎線，閱讀連結斜體收尾。",
            "#2f4858", 62, true, true, ThemePresets.CardStyleList, "serif", "serif", 2.2, 2.1, 1,
            ".site-shell{--radius:4px}.prose h2,.prose h3{letter-spacing:.02em}"
            + ".prose a{text-underline-offset:.35em;text-decoration-thickness:1px}"
            + ".prose blockquote{margin:1.5rem 0;padding:.2rem 0 .2rem 1.2rem;border-left:3px solid var(--accent-ink);color:var(--muted)}"
            + ".read-link{font-style:italic}"
            + ".site-footer{margin-top:3rem;border-top:1px solid var(--border);padding-top:1.4rem}"),
        Theme("headline", "頭版", "雜誌編輯風：無襯線正文配重筆襯線大標題、格狀卡片與大寫字距日期；連結以色塊底線呈現。",
            "#b3261e", 74, true, true, ThemePresets.CardStyleGrid, "sans", "serif", 1.75, 1.5, 1.3,
            ".site-shell{--radius:2px}"
            + ".post-preview time{letter-spacing:.14em;text-transform:uppercase;font-size:.7rem}"
            + ".post-list{border-top:3px solid var(--ink)}"
            + ".prose a{text-decoration:none;border-bottom:2px solid var(--accent-ink)}"
            + ".prose h1,.prose h2{font-weight:800}"
            + ".byline{letter-spacing:.1em;text-transform:uppercase;font-size:.72rem}"),
        Theme("terminal", "終端機", "極客風：等寬字體、直角邊框與日誌式文章卡；標題以 # 前綴、閱讀連結以 > 呈現。",
            "#2ea043", 78, false, false, ThemePresets.CardStyleList, "mono", "mono", 1.7, 1.2, .92,
            ".site-shell{--radius:0;font-variant-ligatures:none}"
            + ".post-list{border-top:0;display:grid;gap:1rem}"
            + ".post-preview{border:1px solid var(--border);padding:1rem 1.2rem}"
            + ".prose h2::before{content:\"# \"}.prose h3::before{content:\"## \"}"
            + ".read-link::before{content:\"> \"}"
            + ".prose code{border:1px solid var(--border);border-radius:0}"),
        Theme("atelier", "畫室", "攝影畫廊風：寬欄大標題、格狀浮起卡片與圓角圖片，以封面圖為視覺主角。",
            "#b45309", 82, false, true, ThemePresets.CardStyleGrid, "sans", "sans", 1.9, 2.3, 1.18,
            ".site-shell{--radius:16px}"
            + ".site-shell[data-card-style=\"grid\"] .post-preview{box-shadow:var(--shadow-raised);padding:1.4rem}"
            + ".prose img{border-radius:14px}"
            + ".post-preview time{font-size:.72rem;letter-spacing:.1em}"
            + ".site-description{letter-spacing:.05em}"
            + "figcaption{font-size:.78rem;color:var(--muted)}"),
        Theme("zen", "侘寂", "日式極簡：安靜的小標題、大留白與髮絲分隔線，色彩低調內斂。",
            "#57534e", 68, false, false, ThemePresets.CardStyleList, "sans", "sans", 2.0, 2.6, .85,
            ".site-shell{--radius:0}"
            + ".post-list{border-top:0}"
            + ".prose h1,.prose h2,.prose h3{font-weight:500;letter-spacing:.05em}"
            + ".prose a{text-decoration-thickness:.5px;text-underline-offset:.5em}"
            + ".read-link{color:var(--muted)}"
            + ".site-footer{letter-spacing:.08em;font-size:.85rem}"),
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
