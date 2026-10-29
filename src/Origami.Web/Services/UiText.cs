namespace Origami.Web.Services;

public static class UiText
{
    private static readonly ResourceManager Resources =
        new("Origami.Web.Resources.UiText", typeof(UiText).Assembly);

    public static string T(string key, string defaultText) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? defaultText;

    /// <summary>帶格式引數的譯文；引數以目前文化格式化。</summary>
    public static string T(string key, string defaultText, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, T(key, defaultText), arguments);
}
