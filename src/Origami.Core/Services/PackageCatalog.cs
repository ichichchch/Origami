namespace Origami.Core.Services;

/// <summary>內建目錄：出廠即有的受控包清單，值全為白名單與界限內常數。
/// 安裝時把宣告完整寫入 SitePackage（站點隔離、隨站匯出），目錄後續調整不影響已安裝站台。</summary>
public static class PackageCatalog
{
    public sealed record CatalogEntry(PackageKind Kind, string Slug, string Name, string Description, PackageManifest Manifest);

    public static readonly IReadOnlyList<CatalogEntry> All =
    [
        Theme("inkwell", "墨井", "文藝經典：襯線正文、首字下沉、細規則線分隔與斜體閱讀連結，欄寬緊湊適合長文。",
            "#2f4858", 62, true, true, ThemePresets.CardStyleList, "serif", "serif", 2.2, 2.1, 1,
            ".site-shell.site-shell{max-width:1040px}"
            + ".site-shell .site-header{min-height:96px;border-bottom-color:var(--border-strong)}"
            + ".site-shell .site-brand{font-size:1.3rem;font-weight:600;letter-spacing:.02em}"
            + ".site-shell .site-nav a{letter-spacing:.08em}"
            + ".site-shell[data-card-style] .post-list{display:block;border-top:1px solid var(--border)}"
            + ".site-shell[data-card-style] .post-preview{grid-template-columns:minmax(0,1fr) auto;gap:.3rem 2.4rem}"
            + ".site-shell .post-preview time{grid-column:1/-1;font-size:.72rem;letter-spacing:.16em;text-transform:uppercase}"
            + ".site-shell .post-preview h2{font-size:calc(1.65rem * var(--heading-scale,1));font-weight:600;letter-spacing:.01em}"
            + ".site-shell .post-preview h2 a{border-bottom:1px solid transparent}"
            + ".site-shell .post-preview h2 a:hover{border-bottom-color:var(--accent-ink)}"
            + ".site-shell .read-link{font-style:italic;font-size:.82rem}"
            + ".site-shell .read-link::after{content:\" →\"}"
            + ".site-shell .prose{font-size:1.06rem}"
            + ".site-shell .prose>p:first-child::first-letter{float:left;font-size:3.2em;line-height:.86;padding:.04em .14em 0 0;color:var(--accent-ink);font-weight:600}"
            + ".site-shell .prose blockquote{margin:1.8rem 0;padding:.2rem 0 .2rem 1.4rem;border-left:3px solid var(--accent-ink);color:var(--muted);font-style:italic}"
            + ".site-shell .prose pre{border-left:3px solid var(--accent-ink)}"
            + ".site-shell .article-heading h1{font-weight:600;letter-spacing:.01em}"
            + ".site-shell .site-footer{border-top-color:var(--border-strong);font-style:italic}"),
        Theme("headline", "頭版", "報紙雜誌：寬版兩欄文章列表、粗分隔線、大寫紅色標籤與重筆標題，資訊密度高。",
            "#b3261e", 74, true, true, ThemePresets.CardStyleGrid, "sans", "serif", 1.75, 1.5, 1.3,
            ".site-shell.site-shell{max-width:1400px}"
            + ".site-shell .site-header{min-height:88px;border-bottom:3px solid var(--ink)}"
            + ".site-shell .site-brand{font-size:1.5rem;font-weight:800;letter-spacing:-.03em}"
            + ".site-shell .site-nav{gap:.5rem 1.8rem}"
            + ".site-shell .site-nav a{font-weight:700;font-size:.76rem;letter-spacing:.14em;text-transform:uppercase;color:var(--ink)}"
            + ".site-shell .public-heading h1{font-weight:800;letter-spacing:-.02em}"
            + ".site-shell .eyebrow{color:var(--accent-ink);letter-spacing:.22em;text-transform:uppercase;font-size:.68rem}"
            + ".site-shell[data-card-style] .post-list{border-top:0;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:0 2.6rem}"
            + ".site-shell[data-card-style] .post-preview{grid-template-columns:minmax(0,1fr) auto;gap:.3rem 1.6rem;padding:1.6rem 0;border:0;border-bottom:1px solid var(--border)}"
            + ".site-shell .post-preview time{grid-column:1/-1;color:var(--accent-ink);font-weight:700;font-size:.7rem;letter-spacing:.18em;text-transform:uppercase}"
            + ".site-shell .post-preview h2{font-weight:800;letter-spacing:-.01em;line-height:1.24;font-size:calc(1.5rem * var(--heading-scale,1))}"
            + ".site-shell .post-preview h2 a:hover{text-decoration:underline;text-decoration-color:var(--accent-ink);text-decoration-thickness:2px}"
            + ".site-shell .read-link{font-weight:700;font-size:.7rem;letter-spacing:.14em;text-transform:uppercase;color:var(--accent-ink)}"
            + ".site-shell .widget{border:1px solid var(--border);border-radius:0;background:transparent}"
            + ".site-shell .site-footer{border-top:3px solid var(--ink);margin-top:3.4rem}"
            + "@media (max-width:860px){.site-shell .post-list{grid-template-columns:1fr}}"),
        Theme("terminal", "終端機", "極客終端：等寬字體、直角邊框日誌卡、提示符前綴與虛線框線，冷靜工程感。",
            "#2ea043", 78, false, false, ThemePresets.CardStyleList, "mono", "mono", 1.7, 1.2, .92,
            ".site-shell.site-shell{max-width:1180px;font-variant-ligatures:none}"
            + ".site-shell .site-header{min-height:72px;border:1px solid var(--border);background:var(--surface);padding:0 1.1rem}"
            + ".site-shell .site-brand{font-size:1.15rem;font-weight:700;letter-spacing:0}"
            + ".site-shell .site-brand::before{content:\"> \";color:var(--accent-ink)}"
            + ".site-shell .site-nav{gap:.4rem 1.2rem}"
            + ".site-shell .site-nav a{font-size:.76rem;letter-spacing:.06em}"
            + ".site-shell .site-nav a::before{content:\"[\"}"
            + ".site-shell .site-nav a::after{content:\"]\"}"
            + ".site-shell[data-card-style] .post-list{display:block;border-top:0}"
            + ".site-shell[data-card-style] .post-preview{grid-template-columns:minmax(0,1fr);gap:.45rem;border:1px solid var(--border);padding:1rem 1.2rem;background:var(--surface);margin-bottom:.8rem}"
            + ".site-shell .post-preview time{font-size:.7rem;letter-spacing:.08em;color:var(--accent-ink)}"
            + ".site-shell .post-preview time::before{content:\"[ \"}"
            + ".site-shell .post-preview time::after{content:\" ]\"}"
            + ".site-shell .post-preview h2{font-size:calc(1.22rem * var(--heading-scale,1));letter-spacing:.02em}"
            + ".site-shell .post-preview h2::before{content:\"# \"}"
            + ".site-shell .post-preview h2 a:hover{text-decoration:underline;text-decoration-style:dashed}"
            + ".site-shell .read-link{text-align:left;font-size:.72rem;letter-spacing:.1em;text-transform:uppercase;color:var(--muted)}"
            + ".site-shell .read-link::after{content:\" [read]\"}"
            + ".site-shell .prose{font-size:.98rem}"
            + ".site-shell .prose h2::before{content:\"## \"}"
            + ".site-shell .prose h3::before{content:\"### \"}"
            + ".site-shell .prose pre,.site-shell .prose code{border:1px solid var(--border);background:var(--code-bg)}"
            + ".site-shell .prose a{text-decoration-style:dashed}"
            + ".site-shell .widget{border:1px solid var(--border);border-radius:0;background:var(--surface)}"
            + ".site-shell .site-footer{border-top:1px dashed var(--border)}"),
        Theme("atelier", "畫室", "攝影畫廊：寬版三欄圖片網格、封面為主視覺、卡片無框線只留留白與陰影。",
            "#b45309", 82, false, true, ThemePresets.CardStyleGrid, "sans", "sans", 1.9, 2.3, 1.18,
            ".site-shell.site-shell{max-width:1440px}"
            + ".site-shell[data-card-style] .post-list{border-top:0;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:2.2rem 1.8rem}"
            + ".site-shell[data-card-style] .post-preview{grid-template-columns:minmax(0,1fr);gap:.75rem;border:0;padding:0;margin-bottom:0}"
            + ".site-shell .post-summary{display:flex;flex-direction:column}"
            + ".site-shell .post-preview .post-cover{order:-1;margin:0 0 .2rem;border-radius:16px;overflow:hidden}"
            + ".site-shell .post-preview .post-cover img{height:240px;object-fit:cover;transition:transform .45s ease}"
            + ".site-shell .post-preview:hover .post-cover img{transform:scale(1.05)}"
            + ".site-shell .post-preview h2{font-weight:600;letter-spacing:-.01em;font-size:calc(1.3rem * var(--heading-scale,1))}"
            + ".site-shell .post-preview .post-summary p{font-size:.9rem}"
            + ".site-shell .read-link{text-align:left;font-size:.72rem;letter-spacing:.12em;text-transform:uppercase;color:var(--accent-ink)}"
            + ".site-shell .layout-columns{grid-template-columns:minmax(0,1fr) 15rem;gap:3.2rem}"
            + ".site-shell .widget{border:0;border-top:1px solid var(--border);border-radius:0;padding:.9rem 0;background:transparent}"
            + ".site-shell .site-footer{border-top:1px solid var(--border);margin-top:4.4rem;padding-top:2.2rem}"
            + "@media (max-width:980px){.site-shell .post-list{grid-template-columns:repeat(2,minmax(0,1fr))}}"
            + "@media (max-width:640px){.site-shell .post-list{grid-template-columns:1fr}}"),
        Theme("zen", "侘寂", "瑞士極簡：窄版置中版面、大留白、細髮絲分隔線與安靜標題，資訊退到背景。",
            "#57534e", 68, false, false, ThemePresets.CardStyleList, "sans", "sans", 2.0, 2.6, .85,
            ".site-shell.site-shell{max-width:900px}"
            + ".site-shell .site-header{flex-direction:column;align-items:center;justify-content:center;gap:1.1rem;text-align:center;min-height:120px}"
            + ".site-shell .site-brand{font-size:1.05rem;font-weight:500;letter-spacing:.32em}"
            + ".site-shell .site-nav{gap:.4rem 1.7rem;justify-content:center}"
            + ".site-shell .site-nav a{font-size:.72rem;letter-spacing:.22em;text-transform:uppercase;color:var(--muted)}"
            + ".site-shell .public-heading,.site-shell .article-heading{text-align:center}"
            + ".site-shell .public-heading h1{font-weight:400;letter-spacing:.08em}"
            + ".site-shell .eyebrow{text-align:center;letter-spacing:.26em;color:var(--muted)}"
            + ".site-shell[data-card-style] .post-list{display:block;border-top:0}"
            + ".site-shell[data-card-style] .post-preview{grid-template-columns:minmax(0,1fr);gap:.4rem;border:0;border-bottom:1px solid var(--border);padding:2.4rem 0;text-align:center}"
            + ".site-shell .post-preview time{font-size:.68rem;letter-spacing:.2em;text-transform:uppercase;color:var(--muted)}"
            + ".site-shell .post-preview h2{font-weight:400;letter-spacing:.05em;font-size:calc(1.42rem * var(--heading-scale,1))}"
            + ".site-shell .post-preview .post-summary p{text-align:center}"
            + ".site-shell .read-link{text-align:center;font-size:.7rem;letter-spacing:.18em;text-transform:uppercase;color:var(--muted)}"
            + ".site-shell .layout-columns{grid-template-columns:minmax(0,1fr)}"
            + ".site-shell .site-sidebar{border-top:1px solid var(--border);padding-top:1.8rem}"
            + ".site-shell .widget{border:0;border-top:1px solid var(--border);border-radius:0;padding:.8rem 0;background:transparent}"
            + ".site-shell .site-footer{border-top:1px solid var(--border);text-align:center;margin-top:4.6rem;padding-top:2.2rem;letter-spacing:.12em}"),
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
