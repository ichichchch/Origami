namespace Origami.Core.Models;

/// <summary>
/// 受控版型：以預設與版型變數表達前台樣式，不載入任何程式碼、不提供自由模板。
/// 預設清單寫在程式內，網站只能在這些預設之間選擇並微調變數。
/// </summary>
public static class ThemePresets
{
    /// <summary>版型預設：除了強調色與欄寬，每個預設自帶字體棧與版面密度變數，
    /// 讓 reading／minimal／gallery 有實質的視覺差異；值全部來自內建常數，不接受使用者輸入。</summary>
    public sealed record Preset(string Name, string Label, string AccentColor, int ReadingWidth,
        bool ShowByline, bool ShowCover, string CardStyle, string FontBody = SansStack,
        string FontHeading = SansStack, double ProseLine = 1.85, double ListGap = 1.6, double HeadingScale = 1);

    /// <summary>卡片樣式：清單（list）或格狀（grid）。</summary>
    public const string CardStyleList = "list";
    public const string CardStyleGrid = "grid";

    private const string SansStack = "\"Segoe UI\", \"Microsoft JhengHei\", \"PingFang TC\", sans-serif";
    private const string SerifStack = "\"Noto Serif TC\", \"PMingLiU\", Georgia, serif";

    /// <summary>找不到預設名稱時的退回對象（reading）。</summary>
    public static Preset Fallback => All[0];

    /// <summary>色彩模式：auto（跟隨系統）、light（淺色）或 dark（深色）。</summary>
    public const string ColorSchemeAuto = "auto";
    public const string ColorSchemeLight = "light";
    public const string ColorSchemeDark = "dark";

    /// <summary>個人外觀偏好的 cookie 名；後台三態切換寫入，站台設為 auto 時以此覆蓋系統探測。</summary>
    public const string ThemeCookieName = "origami.theme";

    public static bool IsValidColorScheme(string? scheme) =>
        scheme is ColorSchemeAuto or ColorSchemeLight or ColorSchemeDark;

    public static readonly IReadOnlyList<Preset> All =
    [
        // 閱讀：襯線正文與標題、寬鬆行距——長文沉浸式閱讀。
        new("reading", "閱讀", "#2f6f4e", 68, true, true, CardStyleList,
            FontBody: SerifStack, FontHeading: SerifStack, ProseLine: 2.0, ListGap: 2.2, HeadingScale: 1),
        // 極簡：無襯線、緊湊行距與更密的列表——文字為主的安靜版面。
        new("minimal", "極簡", "#333333", 60, false, false, CardStyleList,
            FontBody: SansStack, FontHeading: SansStack, ProseLine: 1.7, ListGap: 1.2, HeadingScale: .92),
        // 圖集：無襯線搭配放大標題——以封面圖與格狀卡片為視覺主角。
        new("gallery", "圖集", "#8a5a2b", 72, true, true, CardStyleGrid,
            FontBody: SansStack, FontHeading: SansStack, ProseLine: 1.85, ListGap: 1.6, HeadingScale: 1.15),
    ];

    public static Preset? Find(string? name) =>
        All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>合法的十六進位色碼（# 加三或六位數）；不接受其他格式，避免注入樣式。</summary>
    public static bool IsValidColor(string? color) =>
        color is { Length: 4 or 7 } && color[0] == '#'
        && color[1..].All(Uri.IsHexDigit);

    /// <summary>組出受控版型的 inline CSS 變數：品牌基色、欄寬與預設的字體／密度變數。
    /// 色碼與欄寬先驗證；版面變數全來自內建預設常數，使用者無法注入任意值。</summary>
    public static string StyleVariables(string? accentColor, int readingWidth, string? presetName)
    {
        var color = IsValidColor(accentColor) ? accentColor! : "#2f6f4e";
        var width = IsValidReadingWidth(readingWidth) ? readingWidth : 68;
        var preset = Find(presetName) ?? Fallback;
        return $"--accent-base:{color};--reading-width:{width}ch"
            + $";--font-body:{preset.FontBody};--font-heading:{preset.FontHeading}"
            + $";--prose-line:{preset.ProseLine.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + $";--list-gap:{preset.ListGap.ToString(System.Globalization.CultureInfo.InvariantCulture)}rem"
            + $";--heading-scale:{preset.HeadingScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>可讀範圍內的欄寬；超出即視為無效。</summary>
    public static bool IsValidReadingWidth(int width) => width is >= 48 and <= 96;
}
