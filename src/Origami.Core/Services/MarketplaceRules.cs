namespace Origami.Core.Services;

/// <summary>受控包 manifest 的逐欄位驗證與標準化；所有值皆須是白名單鍵或界限內的宣告資料，
/// 原始 CSS 字體棧字串絕不來自輸入。儲存與渲染（對資料庫值）都經過本類別。</summary>
internal static class MarketplaceRules
{
    /// <summary>主題包寫入站台設定 ThemePreset 欄位的前綴。</summary>
    public const string PresetPrefix = "pkg:";

    /// <summary>manifest 同時接受列舉名稱字串（camelCase）與數字，以字串撰寫較可讀。</summary>
    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static bool IsPackagePreset(string? presetName) =>
        presetName is not null && presetName.StartsWith(PresetPrefix, StringComparison.Ordinal);

    public static string PresetValueOf(string slug) => PresetPrefix + slug;

    public static string SlugOfPreset(string presetName) => presetName[PresetPrefix.Length..];

    /// <summary>解析並驗證 manifest JSON；任何欄位不符即丟出 CmsException，不部分接受。</summary>
    public static PackageManifest Validate(string manifestJson)
    {
        PackageManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<PackageManifest>(manifestJson, ManifestOptions); }
        catch (JsonException) { throw new CmsException("包宣告不是有效的 JSON。"); }
        catch (NotSupportedException) { throw new CmsException("包宣告不是有效的 JSON。"); }
        return ValidateManifest(manifest ?? throw new CmsException("包宣告不是有效的 JSON。"));
    }

    /// <summary>驗證並正規化 manifest；回傳標準物件（slug 小寫、字串修剪、CSS 已清理）。</summary>
    public static PackageManifest ValidateManifest(PackageManifest manifest)
    {
        var slug = (manifest.Slug ?? "").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(slug, @"\A[a-z0-9]([a-z0-9-]{0,38}[a-z0-9])?\z"))
            throw new CmsException($"包代稱須為 1–{Limits.PackageSlug} 個小寫字母、數字或連字號，且以字母數字開頭結尾。");
        if (ThemePresets.Find(slug) is not null)
            throw new CmsException("包代稱不得與內建版型預設同名。");
        var name = (manifest.Name ?? "").Trim();
        if (name.Length == 0 || name.Length > Limits.PackageName)
            throw new CmsException($"包名稱須為 1–{Limits.PackageName} 字。");
        var description = (manifest.Description ?? "").Trim();
        if (description.Length > Limits.PackageDescription)
            throw new CmsException($"包描述最長 {Limits.PackageDescription} 字。");
        ThemeManifest? theme = null;
        List<ManifestWidget>? widgets = null;
        switch (manifest.Kind)
        {
            case PackageKind.Theme:
                if (manifest.Theme is null) throw new CmsException("主題包缺少 theme 宣告區段。");
                theme = ValidateTheme(manifest.Theme);
                break;
            case PackageKind.Widgets:
                if (manifest.Widgets is not { Count: > 0 })
                    throw new CmsException("小工具包須包含至少一個小工具定義。");
                if (manifest.Widgets.Count > Limits.PackageMaxWidgets)
                    throw new CmsException($"單一小工具包最多 {Limits.PackageMaxWidgets} 個項目。");
                widgets = [.. manifest.Widgets.Select(ValidateWidget)];
                break;
            default:
                throw new CmsException("包種類無效（僅接受 theme 或 widgets）。");
        }
        return new PackageManifest(slug, name, description, manifest.Kind, theme, widgets);
    }

    /// <summary>驗證後重新序列化：落庫值一律來自本方法，確保儲存與匯出的是同一種標準形狀。</summary>
    public static string Canonicalize(PackageManifest manifest) =>
        JsonSerializer.Serialize(manifest, ManifestOptions);

    private static ThemeManifest ValidateTheme(ThemeManifest theme)
    {
        var accentColor = (theme.AccentColor ?? "").Trim();
        if (!ThemePresets.IsValidColor(accentColor))
            throw new CmsException("主題包強調色須為 # 加三或六位十六進位數字。");
        if (!ThemePresets.IsValidReadingWidth(theme.ReadingWidth))
            throw new CmsException("主題包欄寬須為 48–96 字。");
        var cardStyle = (theme.CardStyle ?? "").Trim().ToLowerInvariant();
        if (cardStyle is not (ThemePresets.CardStyleList or ThemePresets.CardStyleGrid))
            throw new CmsException("主題包卡片樣式無效（僅接受 list 或 grid）。");
        if (!ThemePresets.IsValidFontStackKey(theme.FontBody) || !ThemePresets.IsValidFontStackKey(theme.FontHeading))
            throw new CmsException($"主題包字體棧僅接受白名單鍵：{string.Join("、", ThemePresets.FontStacks.Keys)}。");
        if (theme.ProseLine is < 1.2 or > 2.6)
            throw new CmsException("主題包行距須為 1.2–2.6。");
        if (theme.ListGap is < 0.6 or > 3.0)
            throw new CmsException("主題包列表間距須為 0.6–3.0。");
        if (theme.HeadingScale is < 0.75 or > 1.5)
            throw new CmsException("主題包標題比例須為 0.75–1.5。");
        var css = (theme.Css ?? "").Trim();
        if (css.Length == 0) css = null;
        else
        {
            if (css.Length > Limits.CustomCss)
                throw new CmsException($"主題包 CSS 最長 {Limits.CustomCss} 字。");
            css = CssSanitizer.CleanOrThrow(css);
        }
        return theme with
        {
            AccentColor = accentColor.ToLowerInvariant(),
            CardStyle = cardStyle,
            Css = css
        };
    }

    private static ManifestWidget ValidateWidget(ManifestWidget widget)
    {
        if (!Enum.IsDefined(widget.Area)) throw new CmsException("小工具區域無效。");
        if (!WidgetTypes.IsValid(widget.Type)) throw new CmsException("小工具型別無效。");
        var title = (widget.Title ?? "").Trim();
        if (title.Length > Limits.WidgetTitle)
            throw new CmsException($"小工具標題最長 {Limits.WidgetTitle} 字。");
        var (count, markdown, url) = WidgetService.NormalizeConfig(widget.Type, widget.Count, widget.Markdown, widget.Url);
        return widget with
        {
            Title = title,
            Count = count,
            Markdown = markdown.Length == 0 ? null : markdown,
            Url = url.Length == 0 ? null : url
        };
    }

    /// <summary>把驗證過的主題包宣告轉為內建預設同款的 Preset；字體棧值由白名單鍵解析為內建常數。</summary>
    public static ThemePresets.Preset ToPreset(PackageManifest manifest) =>
        manifest.Theme is not { } theme
            ? throw new InvalidOperationException("僅主題包可轉為版型預設。")
            : new ThemePresets.Preset(PresetValueOf(manifest.Slug), manifest.Name, theme.AccentColor, theme.ReadingWidth,
                theme.ShowByline, theme.ShowCover, theme.CardStyle,
                ThemePresets.FontStacks[theme.FontBody], ThemePresets.FontStacks[theme.FontHeading],
                theme.ProseLine, theme.ListGap, theme.HeadingScale);
}
