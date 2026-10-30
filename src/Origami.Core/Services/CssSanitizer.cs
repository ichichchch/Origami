namespace Origami.Core.Services;

/// <summary>
/// 自訂 CSS 的受控清理：不解析樣式語意，而是先移除註解，再以黑名單拒絕危險構造、
/// 以白名單放行 at-rule。儲存路徑丟出清楚的 CmsException；輸出路徑以 TryClean 把關，
/// 清理失敗時整段丟棄，絕不把未清理的內容送到瀏覽器。
/// 僅站點管理員可編輯此欄位；url() 允許指向站內或外部圖片資源，與內文圖片同一風險邊界。
/// </summary>
public static partial class CssSanitizer
{
    /// <summary>允許的 at-rule：排版、查詢與動畫；可對外載入資源的（@import、@font-face 等）一律拒絕。</summary>
    private static readonly HashSet<string> AllowedAtRules = new(StringComparer.OrdinalIgnoreCase)
        { "media", "supports", "keyframes", "-webkit-keyframes", "layer", "container", "property", "scope" };

    /// <summary>黑名單 token 全部小寫比對；反斜線也拒絕，避免以 CSS 轉義 smuggle 危險字串。</summary>
    private static readonly string[] ForbiddenTokens =
        ["expression(", "javascript:", "vbscript:", "behavior:", "-moz-binding", "binding:", "</", "<!--", "\\"];

    [GeneratedRegex(@"(?s)/\*.*?\*/", RegexOptions.CultureInvariant)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"(?<![\w-])@([a-zA-Z-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AtRuleRegex();

    /// <summary>輸出前清理：失敗時回 false 且 clean 為空字串，呼叫端以此決定不輸出。</summary>
    public static bool TryClean(string? css, out string clean)
    {
        var (ok, value, _) = Clean(css);
        clean = ok ? value : "";
        return ok;
    }

    /// <summary>儲存前清理：危險構造以明確訊息拒絕，不靜默改寫使用者的樣式。</summary>
    public static string CleanOrThrow(string? css)
    {
        var (ok, value, reason) = Clean(css);
        if (!ok) throw new CmsException(reason);
        return value;
    }

    private static (bool Ok, string Clean, string Reason) Clean(string? css)
    {
        var text = (css ?? "").Trim();
        if (text.Length == 0) return (true, "", "");
        if (text.Length > Limits.CustomCss)
            return (false, "", $"自訂 CSS 長度超過上限（最多 {Limits.CustomCss} 字元）。");
        // 註解先移除再掃描，避免以 /* ... */ 藏匿危險構造。
        text = CommentRegex().Replace(text, " ").Trim();
        foreach (var ch in text)
        {
            // 控制字元僅允許定位與換行；其餘（含 ESC、NUL）一律拒絕。
            if (char.IsControl(ch) && ch is not ('\t' or '\r' or '\n'))
                return (false, "", "自訂 CSS 不得包含控制字元。");
        }
        var lowered = text.ToLowerInvariant();
        foreach (var token in ForbiddenTokens)
        {
            if (lowered.Contains(token, StringComparison.Ordinal))
                return (false, "", $"自訂 CSS 包含不支援的構造（{token.TrimEnd('(')}）；僅接受純樣式規則。");
        }
        foreach (Match match in AtRuleRegex().Matches(text))
        {
            var name = match.Groups[1].Value;
            if (!AllowedAtRules.Contains(name))
                return (false, "", $"不支援的 at-rule：@{name}（僅允許 media、supports、keyframes、layer、container、property、scope）。");
        }
        return (true, text, "");
    }
}
