namespace Origami.Web.Services;

using Origami.Core.Models;

/// <summary>介面語言清單；語言鍵的單一真相來源在 Core 的 SupportedUiLanguages，此處補上主機概念（Cookie 與預設值）。</summary>
public static class UiTextLanguages
{
    public const string CookieName = "origami.lang";
    public const string Default = "zh-Hant";

    public static readonly IReadOnlyList<string> Supported = SupportedUiLanguages.All;

    public static string? Normalize(string? culture) => SupportedUiLanguages.Normalize(culture);

    public static bool IsSupported(string? culture) => Normalize(culture) is not null;
}
