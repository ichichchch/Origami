namespace Origami.Core.Services;

/// <summary>
/// 受控包市場：瀏覽內建目錄、上傳 ZIP 安裝、啟用／停用與解除。商品為宣告式擴充包
/// （主題包＝版型宣告；小工具包＝內建型別的預先組合），不載入任何程式碼。
/// 主題包啟用即寫入站台設定的版型預設（pkg: 前綴，單一真相來源）；
/// 小工具包安裝即物化為站點小工具（帶包標記），停用只影響公開投影、解除時連同刪除。
/// </summary>
public sealed class MarketplaceService(
    IDbContextFactory<CmsDbContext> factory,
    AdminAccess admin,
    TimeProvider? clock = null,
    CmsEventBus? events = null)
{
    public sealed record ThemeSummary(string AccentColor, int ReadingWidth, bool ShowByline, bool ShowCover,
        string CardStyle, string FontBody, string FontHeading, double ProseLine, double ListGap, double HeadingScale);

    public sealed record WidgetItemSummary(WidgetArea Area, WidgetType Type, string Title, int? Count, string Markdown, string Url);

    /// <summary>已安裝包的檢視；WidgetCount 為資料庫實際小工具數（小工具包）。</summary>
    public sealed record PackageView(Guid Id, PackageKind Kind, string Slug, string Name, string Description,
        bool IsBuiltIn, bool IsEnabled, DateTimeOffset InstalledAt, Guid Version,
        ThemeSummary? Theme, int WidgetCount, List<WidgetItemSummary> Widgets);

    /// <summary>ZIP 解析預覽；未落地、無副作用。</summary>
    public sealed record ManifestPreview(PackageKind Kind, string Slug, string Name, string Description,
        ThemeSummary? Theme, List<WidgetItemSummary> Widgets);

    /// <summary>內建目錄條目檢視；攜帶宣告摘要供目錄卡片預覽。</summary>
    public sealed record CatalogEntryView(PackageKind Kind, string Slug, string Name, string Description, bool IsInstalled,
        ThemeSummary? Theme, List<WidgetItemSummary> Widgets);

    /// <summary>公開投影用的現行主題包；字體棧為內建常數值，CSS 已再次清理。</summary>
    public sealed record ActiveTheme(string Slug, string FontBody, string FontHeading,
        double ProseLine, double ListGap, double HeadingScale, string Css);

    /// <summary>已安裝清單（含 manifest 摘要）；儲存的 manifest 解析失敗時摘要為空，仍可解除安裝。</summary>
    public async Task<List<PackageView>> InstalledAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.SitePackages.AsNoTracking()
            .OrderBy(x => x.Kind).ThenBy(x => x.Slug).ToListAsync(ct);
        var widgetCounts = await db.SiteWidgets.AsNoTracking()
            .Where(x => x.PackageId != null)
            .GroupBy(x => x.PackageId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key!.Value, x => x.Count, ct);
        return [.. rows.Select(x => ToView(x, widgetCounts.GetValueOrDefault(x.Id)))];
    }

    /// <summary>內建目錄＋各站安裝狀態。</summary>
    public async Task<List<CatalogEntryView>> CatalogAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var installedSlugs = await db.SitePackages.AsNoTracking()
            .Select(x => x.Slug).ToHashSetAsync(StringComparer.Ordinal, ct);
        return [.. PackageCatalog.All.Select(x =>
            new CatalogEntryView(x.Kind, x.Slug, x.Name, x.Description, installedSlugs.Contains(x.Slug),
                x.Manifest.Theme is { } theme
                    ? new ThemeSummary(theme.AccentColor, theme.ReadingWidth, theme.ShowByline, theme.ShowCover,
                        theme.CardStyle, theme.FontBody, theme.FontHeading, theme.ProseLine, theme.ListGap, theme.HeadingScale)
                    : null,
                [.. (x.Manifest.Widgets ?? []).Select(w => new WidgetItemSummary(w.Area, w.Type, w.Title ?? "",
                    w.Count, w.Markdown ?? "", w.Url ?? ""))]))];
    }

    /// <summary>解析包 ZIP 供預覽；不落地任何資料。</summary>
    public async Task<ManifestPreview> AnalyzeZipAsync(Stream input, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var manifest = await ReadPackageZipAsync(input, ct);
        return ToPreview(manifest);
    }

    public async Task<PackageView> InstallFromCatalogAsync(string slug, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var entry = PackageCatalog.Find(slug ?? "") ?? throw new CmsException("內建目錄中沒有這個包。");
        return await InstallAsync(entry.Manifest, PackageSource.BuiltIn, ct);
    }

    public async Task<PackageView> InstallFromZipAsync(Stream input, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var manifest = await ReadPackageZipAsync(input, ct);
        return await InstallAsync(manifest, PackageSource.Upload, ct);
    }

    /// <summary>啟用／停用。主題包啟用即套用版型（寫入設定並複製純量欄位），停用退回閱讀預設；
    /// 小工具包只影響公開投影過濾，不動已建立的小工具資料列。</summary>
    public async Task<PackageView> SetEnabledAsync(Guid id, bool enabled, Guid version, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var access = await admin.GetCurrentAccessAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var package = await db.SitePackages.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CmsException("受控包不存在。");
        if (package.Version != version)
            throw new CmsException("資料已被另一個分頁更新，請重新載入後再操作。");
        db.Entry(package).Property(x => x.Version).OriginalValue = version;
        if (package.Kind == PackageKind.Theme)
        {
            var settings = await db.Settings.SingleAsync(x => x.Id == 1, ct);
            if (enabled)
            {
                // 啟用需要有效的包宣告；停用只比對代稱，宣告損毀時仍可停用或解除。
                var manifest = MarketplaceRules.Validate(package.ManifestJson);
                await ApplyThemeSelectionAsync(db, package, manifest, settings, ct);
            }
            else if (settings.ThemePreset == MarketplaceRules.PresetValueOf(package.Slug))
            {
                settings.ThemePreset = ThemePresets.Fallback.Name;
                settings.Version = Guid.NewGuid();
            }
        }
        package.IsEnabled = enabled;
        package.Version = Guid.NewGuid();
        db.AuditLogs.Add(new AuditLog
        {
            SiteId = db.CurrentSiteId,
            UserId = access.UserId ?? "",
            UserName = access.UserName ?? "",
            Action = enabled ? "package.enabled" : "package.disabled",
            Target = package.Name,
            CreatedAt = (clock ?? TimeProvider.System).GetUtcNow()
        });
        await ContentRules.SaveAsync(db, ct);
        await PublishAsync(enabled ? "package.enabled" : "package.disabled", db.CurrentSiteId, package.Slug, ct);
        var widgetCount = package.Kind == PackageKind.Widgets
            ? await db.SiteWidgets.AsNoTracking().CountAsync(x => x.PackageId == package.Id, ct) : 0;
        return ToView(package, widgetCount);
    }

    /// <summary>解除安裝：小工具包連同刪除其建立的小工具；主題包若為現行版型則退回閱讀預設。</summary>
    public async Task UninstallAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var access = await admin.GetCurrentAccessAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var package = await db.SitePackages.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CmsException("受控包不存在。");
        if (package.Version != version)
            throw new CmsException("資料已被另一個分頁更新，請重新載入後再操作。");
        db.Entry(package).Property(x => x.Version).OriginalValue = version;
        if (package.Kind == PackageKind.Widgets)
        {
            var widgets = await db.SiteWidgets.Where(x => x.PackageId == package.Id).ToListAsync(ct);
            db.SiteWidgets.RemoveRange(widgets);
        }
        else
        {
            var settings = await db.Settings.SingleAsync(x => x.Id == 1, ct);
            if (settings.ThemePreset == MarketplaceRules.PresetValueOf(package.Slug))
            {
                settings.ThemePreset = ThemePresets.Fallback.Name;
                settings.Version = Guid.NewGuid();
            }
        }
        db.SitePackages.Remove(package);
        db.AuditLogs.Add(new AuditLog
        {
            SiteId = db.CurrentSiteId,
            UserId = access.UserId ?? "",
            UserName = access.UserName ?? "",
            Action = "package.uninstalled",
            Target = package.Name,
            CreatedAt = (clock ?? TimeProvider.System).GetUtcNow()
        });
        await ContentRules.SaveAsync(db, ct);
        await PublishAsync("package.uninstalled", db.CurrentSiteId, package.Slug, ct);
    }

    /// <summary>公開投影的現行主題包：對資料庫值重跑驗證（不可信），查無或驗證失敗回 null，
    /// 前台退回內建預設，絕不輸出未驗證的值。</summary>
    public async Task<ActiveTheme?> ActiveThemeAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var presetName = await db.Settings.AsNoTracking().Where(x => x.Id == 1)
            .Select(x => x.ThemePreset).SingleOrDefaultAsync(ct);
        if (!MarketplaceRules.IsPackagePreset(presetName)) return null;
        var slug = MarketplaceRules.SlugOfPreset(presetName!);
        var manifestJson = await db.SitePackages.AsNoTracking()
            .Where(x => x.Kind == PackageKind.Theme && x.Slug == slug)
            .Select(x => x.ManifestJson).SingleOrDefaultAsync(ct);
        if (manifestJson is null) return null;
        try
        {
            var manifest = MarketplaceRules.Validate(manifestJson);
            if (manifest.Theme is not { } theme) return null;
            var css = theme.Css is { Length: > 0 } && CssSanitizer.TryClean(theme.Css, out var clean) ? clean : "";
            return new ActiveTheme(manifest.Slug,
                ThemePresets.FontStacks[theme.FontBody], ThemePresets.FontStacks[theme.FontHeading],
                theme.ProseLine, theme.ListGap, theme.HeadingScale, css);
        }
        catch (CmsException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private async Task<PackageView> InstallAsync(PackageManifest manifest, PackageSource source, CancellationToken ct)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var access = await admin.GetCurrentAccessAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.SitePackages.CountAsync(x => true, ct) >= Limits.MaxInstalledPackages)
            throw new CmsException($"單站最多安裝 {Limits.MaxInstalledPackages} 個受控包。");
        if (await db.SitePackages.AnyAsync(x => x.Slug == manifest.Slug, ct))
            throw new CmsException("此包已安裝。");
        var package = new SitePackage
        {
            Kind = manifest.Kind,
            Slug = manifest.Slug,
            Name = manifest.Name,
            ManifestJson = MarketplaceRules.Canonicalize(manifest),
            Source = source,
            // 小工具包安裝即生效（其小工具立刻出現在公開頁面）；主題包不改變現行版型，
            // 須由管理者另行啟用才會套用。
            IsEnabled = manifest.Kind == PackageKind.Widgets,
            InstalledAt = (clock ?? TimeProvider.System).GetUtcNow()
        };
        db.SitePackages.Add(package);
        if (manifest.Kind == PackageKind.Widgets && manifest.Widgets is not null)
        {
            // 安裝即物化：逐區域檢查剩餘容量，不足即整包拒絕（單一 SaveChanges 交易回滾）。
            foreach (var group in manifest.Widgets.GroupBy(x => x.Area))
            {
                var inArea = await db.SiteWidgets.CountAsync(x => x.Area == group.Key, ct);
                if (inArea + group.Count() > Limits.MaxWidgetsPerArea)
                    throw new CmsException($"區域 {group.Key} 的小工具數量將超過上限 {Limits.MaxWidgetsPerArea}，無法安裝此包。");
            }
            foreach (var group in manifest.Widgets.GroupBy(x => x.Area))
            {
                var position = await db.SiteWidgets.AnyAsync(x => x.Area == group.Key, ct)
                    ? await db.SiteWidgets.Where(x => x.Area == group.Key).MaxAsync(x => x.Position, ct) + 1 : 0;
                foreach (var widget in group)
                {
                    db.SiteWidgets.Add(new SiteWidget
                    {
                        Area = widget.Area,
                        Type = widget.Type,
                        Title = widget.Title ?? "",
                        ConfigJson = WidgetTypes.SerializeConfig(new WidgetTypes.WidgetConfig(
                            widget.Count, widget.Markdown, widget.Url)),
                        IsVisible = true,
                        Position = position++,
                        PackageId = package.Id
                    });
                }
            }
        }
        db.AuditLogs.Add(new AuditLog
        {
            SiteId = db.CurrentSiteId,
            UserId = access.UserId ?? "",
            UserName = access.UserName ?? "",
            Action = "package.installed",
            Target = package.Name,
            CreatedAt = (clock ?? TimeProvider.System).GetUtcNow()
        });
        await ContentRules.SaveAsync(db, ct);
        await PublishAsync("package.installed", db.CurrentSiteId, package.Slug, ct);
        return ToView(package, manifest.Widgets?.Count ?? 0);
    }

    /// <summary>主題包啟用：版型預設改為 pkg: 前綴值（單一真相來源），純量欄位複製自包宣告
    ///（與設定頁切換內建預設的既有語意一致），其他主題包的啟用鏡像同步清除。</summary>
    private static async Task ApplyThemeSelectionAsync(CmsDbContext db, SitePackage package,
        PackageManifest manifest, SiteSettings settings, CancellationToken ct)
    {
        if (manifest.Theme is not { } theme)
            throw new CmsException("主題包宣告無效，請重新安裝。");
        settings.ThemePreset = MarketplaceRules.PresetValueOf(package.Slug);
        settings.AccentColor = theme.AccentColor;
        settings.ReadingWidth = theme.ReadingWidth;
        settings.ShowByline = theme.ShowByline;
        settings.ShowCover = theme.ShowCover;
        settings.CardStyle = theme.CardStyle;
        settings.Version = Guid.NewGuid();
        var otherThemes = await db.SitePackages
            .Where(x => x.Kind == PackageKind.Theme && x.Id != package.Id).ToListAsync(ct);
        foreach (var other in otherThemes)
        {
            other.IsEnabled = false;
            other.Version = Guid.NewGuid();
        }
    }

    /// <summary>包 ZIP 的唯一合法形態：根目錄單一 manifest.json 條目；其餘條目、缺檔或目錄條目一律拒絕。
    /// 壓縮檔與解壓量雙上限，全程記憶體內處理、無檔案落地。</summary>
    private static async Task<PackageManifest> ReadPackageZipAsync(Stream input, CancellationToken ct)
    {
        using var payload = await ArchiveGuard.BufferAsync(input, Limits.PackageZipMaxBytes,
            $"包檔大小須為 1 位元組至 {Limits.PackageZipMaxBytes} 位元組。", ct);
        var bytes = payload.GetBuffer();
        if (payload.Length < 2 || bytes[0] != (byte)'P' || bytes[1] != (byte)'K')
            throw new CmsException("包檔必須是內含 manifest.json 的 ZIP。");
        payload.Position = 0;
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new CmsException("包 ZIP 缺少根目錄的 manifest.json。");
        // 目錄條目或任何其他檔案都構成整體拒絕：包不攜帶二進位資源。
        if (archive.Entries.Count > 1)
            throw new CmsException("包 ZIP 僅接受單一 manifest.json 條目，不得夾帶其他檔案。");
        await using var json = manifestEntry.Open();
        using var expanded = new MemoryStream();
        // 高壓縮比的偽造條目可能在解壓時爆炸；以 manifest 上限邊解壓邊計量。
        await ArchiveGuard.CopyBoundedAsync(json, expanded, 0, Limits.PackageManifestJson,
            $"manifest.json 解壓後超過 {Limits.PackageManifestJson} 位元組上限。", ct);
        expanded.Position = 0;
        using var reader = new StreamReader(expanded);
        return MarketplaceRules.Validate(await reader.ReadToEndAsync(ct));
    }

    private static PackageView ToView(SitePackage package, int widgetCount)
    {
        PackageManifest? manifest = null;
        try { manifest = MarketplaceRules.Validate(package.ManifestJson); }
        catch (CmsException) { /* 儲存值解析失敗：保留可解除的清單項目，摘要留空。 */ }
        return new PackageView(package.Id, package.Kind, package.Slug, package.Name,
            manifest?.Description ?? "", package.Source == PackageSource.BuiltIn, package.IsEnabled,
            package.InstalledAt, package.Version,
            manifest?.Theme is { } theme
                ? new ThemeSummary(theme.AccentColor, theme.ReadingWidth, theme.ShowByline, theme.ShowCover,
                    theme.CardStyle, theme.FontBody, theme.FontHeading, theme.ProseLine, theme.ListGap, theme.HeadingScale)
                : null,
            widgetCount,
            [.. (manifest?.Widgets ?? []).Select(w => new WidgetItemSummary(w.Area, w.Type, w.Title ?? "",
                w.Count, w.Markdown ?? "", w.Url ?? ""))]);
    }

    private static ManifestPreview ToPreview(PackageManifest manifest) =>
        new(manifest.Kind, manifest.Slug, manifest.Name, manifest.Description,
            manifest.Theme is { } theme
                ? new ThemeSummary(theme.AccentColor, theme.ReadingWidth, theme.ShowByline, theme.ShowCover,
                    theme.CardStyle, theme.FontBody, theme.FontHeading, theme.ProseLine, theme.ListGap, theme.HeadingScale)
                : null,
            [.. (manifest.Widgets ?? []).Select(w => new WidgetItemSummary(w.Area, w.Type, w.Title ?? "",
                w.Count, w.Markdown ?? "", w.Url ?? ""))]);

    private async Task PublishAsync(string name, Guid siteId, string slug, CancellationToken ct)
    {
        if (events is not null)
            await events.PublishAsync(name, siteId, new { slug }, ct);
    }
}
