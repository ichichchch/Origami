namespace Origami.Core.Services;

/// <summary>
/// WordPress 主題封裝的唯讀解析：只讀取 style.css 標頭的宣告式中繼資料，並從樣式推導可驗證的
/// 視覺參數（強調色與字體棧鍵）。模板與程式碼一律不保留、不解析、不執行。
/// </summary>
internal static class WordPressThemeReader
{
    /// <summary>解析後的主題資訊；所有欄位皆為宣告式資料，可安全轉為受控主題包。</summary>
    public sealed record ThemeInfo(string Name, string Version, string Author, string Description, string License,
        string Tags, string AccentColor, string FontBody, string FontHeading);

    /// <summary>樣式檔中繼資料標頭的欄位名稱（大小寫不拘）。</summary>
    private static readonly string[] HeaderKeys =
        ["Theme Name", "Theme URI", "Author", "Author URI", "Description", "Version",
            "License", "License URI", "Text Domain", "Tags"];

    /// <summary>讀取 WordPress 主題 ZIP；style.css 標頭缺少主題名稱即拒絕。</summary>
    public static async Task<ThemeInfo> Read(MemoryStream payload)
    {
        payload.Position = 0;
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
        var styleEntry = archive.Entries.FirstOrDefault(x =>
            x.FullName.EndsWith("/style.css", StringComparison.OrdinalIgnoreCase)
            && x.FullName.Count(c => c == '/') == 1)
            ?? archive.Entries.FirstOrDefault(x => x.FullName.Equals("style.css", StringComparison.OrdinalIgnoreCase))
            ?? throw new CmsException("主題封裝缺少 style.css，無法辨識其宣告。");
        using var reader = new StreamReader(styleEntry.Open());
        var css = await ReadBoundedAsync(reader);
        var header = ParseHeader(css);
        var name = header.GetValueOrDefault("Theme Name")?.Trim() ?? "";
        if (name.Length == 0)
            throw new CmsException("style.css 缺少 Theme Name，無法辨識主題名稱。");
        var description = (header.GetValueOrDefault("Description") ?? "").Trim();
        return new ThemeInfo(
            name,
            (header.GetValueOrDefault("Version") ?? "").Trim(),
            (header.GetValueOrDefault("Author") ?? "").Trim(),
            description.Length > Limits.PackageDescription ? description[..Limits.PackageDescription] : description,
            (header.GetValueOrDefault("License") ?? "").Trim(),
            (header.GetValueOrDefault("Tags") ?? "").Trim(),
            DeriveAccentColor(css),
            DeriveFontKey(css),
            DeriveFontKey(css));
    }

    /// <summary>讀取 style.css 內容；樣式檔只讀有限量，避免超大檔案進入記憶體。</summary>
    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[8192];
        var builder = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0) break;
            builder.Append(buffer, 0, read);
            if (builder.Length > Limits.WordPressThemeCssBytes)
                throw new CmsException($"style.css 超過 {Limits.WordPressThemeCssBytes} 字上限，無法解析。");
        }
        return builder.ToString();
    }

    /// <summary>解析標頭註解中的「欄位: 值」區塊；註解外的內容不視為宣告。</summary>
    private static Dictionary<string, string> ParseHeader(string css)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var start = css.IndexOf("/*", StringComparison.Ordinal);
        if (start < 0) return result;
        var end = css.IndexOf("*/", start, StringComparison.Ordinal);
        if (end < 0) return result;
        var block = css[start..end];
        foreach (var line in block.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length == 0 || !HeaderKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
            result[key] = value;
        }
        return result;
    }

    /// <summary>推導強調色：樣式中出現最多次的非灰階色碼；沒有可用色碼時退回內建預設色。</summary>
    private static string DeriveAccentColor(string css)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(css, @"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b"))
        {
            var raw = match.Value.ToLowerInvariant();
            var hex = raw.Length == 4
                ? $"#{raw[1]}{raw[1]}{raw[2]}{raw[2]}{raw[3]}{raw[3]}"
                : raw;
            if (IsNeutral(hex)) continue;
            counts[hex] = counts.GetValueOrDefault(hex) + 1;
        }
        if (counts.Count == 0) return "#2f6f4e";
        return counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;
    }

    /// <summary>灰階與黑白不作為強調色。</summary>
    private static bool IsNeutral(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        if (r == g && g == b) return true;
        // 極低飽和者同樣視為中性色。
        return Math.Abs(r - g) < 12 && Math.Abs(g - b) < 12 && Math.Abs(r - b) < 12;
    }

    /// <summary>推導字體棧鍵：依樣式中出現的 font-family 家族名稱對應白名單鍵；
    /// 對應不到時退回 sans。</summary>
    private static string DeriveFontKey(string css)
    {
        var families = Regex.Matches(css, @"font-family\s*:\s*([^;}]+)", RegexOptions.IgnoreCase)
            .Select(x => x.Groups[1].Value).ToList();
        foreach (var family in families)
        {
            var value = family.ToLowerInvariant();
            if (value.Contains("monospace") || value.Contains("consolas") || value.Contains("monaco")
                || value.Contains("courier") || value.Contains("menlo"))
                return "mono";
            if (value.Contains("serif") && !value.Contains("sans-serif"))
                return "serif";
        }
        return "sans";
    }
}
