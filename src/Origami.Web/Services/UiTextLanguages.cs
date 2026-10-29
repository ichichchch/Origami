namespace Origami.Web.Services;

/// <summary>介面語言清單；主機預設為繁體中文，另提供英文。</summary>
public static class UiTextLanguages
{
    public const string CookieName = "origami.lang";
    public const string Default = "zh-Hant";

    public static readonly IReadOnlyList<string> Supported = Array.AsReadOnly(new[] { "zh-Hant", "en" });

    public static string? Normalize(string? culture) =>
        Supported.FirstOrDefault(value => string.Equals(value, culture, StringComparison.OrdinalIgnoreCase));

    public static bool IsSupported(string? culture) => Normalize(culture) is not null;
}
