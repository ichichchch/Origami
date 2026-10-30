namespace Origami.Core.Models;

/// <summary>
/// 受控版型：以預設與版型變數表達前台樣式，不載入任何程式碼、不提供自由模板。
/// 預設清單寫在程式內，網站只能在這些預設之間選擇並微調變數。
/// </summary>
public static class ThemePresets
{
    public sealed record Preset(string Name, string Label, string AccentColor, int ReadingWidth,
        bool ShowByline, bool ShowCover, string CardStyle);

    /// <summary>卡片樣式：清單（list）或格狀（grid）。</summary>
    public const string CardStyleList = "list";
    public const string CardStyleGrid = "grid";

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
        new("reading", "閱讀", "#2f6f4e", 68, true, true, CardStyleList),
        new("minimal", "極簡", "#333333", 60, false, false, CardStyleList),
        new("gallery", "圖集", "#8a5a2b", 72, true, true, CardStyleGrid),
    ];

    public static Preset? Find(string? name) =>
        All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>合法的十六進位色碼（# 加三或六位數）；不接受其他格式，避免注入樣式。</summary>
    public static bool IsValidColor(string? color) =>
        color is { Length: 4 or 7 } && color[0] == '#'
        && color[1..].All(Uri.IsHexDigit);

    /// <summary>可讀範圍內的欄寬；超出即視為無效。</summary>
    public static bool IsValidReadingWidth(int width) => width is >= 48 and <= 96;
}
