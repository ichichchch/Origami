namespace Origami.Tests;

/// <summary>
/// 受控導覽：站內頁面與外部連結項目、排序與隱藏、預設導覽回退、跨站隔離、匯出匯入往返。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NavigationServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private async Task<Guid> CreatePageAsync(string title, string slug)
    {
        using var admin = await App.AdministratorAsync();
        var page = NewContent(title, kind: ContentKind.Page, status: ContentStatus.Published);
        page.Slug = slug;
        return (await admin.Content.SaveAsync(page)).Id!.Value;
    }

    private async Task EnableCustomNavigationAsync()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.CustomNavigationEnabled = true;
        await admin.Settings.SaveAsync(settings);
    }

    [PostgresFact]
    public async Task Custom_items_drive_the_public_navigation_in_order()
    {
        using var admin = await App.AdministratorAsync();
        var pageId = await CreatePageAsync("關於我們", "about");
        await EnableCustomNavigationAsync();

        var navigation = admin.Services.GetRequiredService<NavigationService>();
        var first = await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "關於我們", ContentId = pageId
        });
        var second = await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "外部網站", Url = "https://example.com/docs"
        });
        var hidden = await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "隱藏項目", Url = "https://example.com/hidden", IsVisible = false
        });

        var entries = await navigation.PublicAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("關於我們", entries[0].Title);
        Assert.Equal("/pages/about", entries[0].Url);
        Assert.False(entries[0].IsExternal);
        Assert.Equal("https://example.com/docs", entries[1].Url);
        Assert.True(entries[1].IsExternal);
        Assert.DoesNotContain(entries, x => x.Id == hidden.Id);

        // 排序：把外部連結調到前面。
        var results = await navigation.ReorderAsync([(second.Id, second.Version), (first.Id, first.Version)]);
        Assert.All(results, x => Assert.True(x.Succeeded));
        var reordered = await navigation.PublicAsync();
        Assert.Equal("外部網站", reordered[0].Title);
    }

    [PostgresFact]
    public async Task Invalid_targets_and_urls_are_refused()
    {
        using var admin = await App.AdministratorAsync();
        var navigation = admin.Services.GetRequiredService<NavigationService>();

        await Assert.ThrowsAsync<CmsException>(() => navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "危險連結", Url = "javascript:alert(1)"
        }));
        await Assert.ThrowsAsync<CmsException>(() => navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "相對路徑", Url = "/relative/path"
        }));
        await Assert.ThrowsAsync<CmsException>(() => navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "缺少目標"
        }));
        await Assert.ThrowsAsync<CmsException>(() => navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "", Url = "https://example.com"
        }));

        // 目標必須是已發布頁面：草稿頁不可被引用。
        var draft = NewContent("草稿頁", kind: ContentKind.Page);
        draft.Slug = "draft-page";
        var saved = await admin.Content.SaveAsync(draft);
        await Assert.ThrowsAsync<CmsException>(() => navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "草稿頁", ContentId = saved.Id
        }));
    }

    [PostgresFact]
    public async Task Default_navigation_lists_published_pages_when_custom_navigation_is_off()
    {
        await CreatePageAsync("甲頁面", "jia");
        await CreatePageAsync("乙頁面", "yi");
        using var admin = await App.AdministratorAsync();
        var navigation = admin.Services.GetRequiredService<NavigationService>();

        var entries = await navigation.PublicAsync();
        Assert.Equal(2, entries.Count);
        // 預設依標題排序（實際排序由資料庫定序決定），且不帶自訂網址。
        Assert.Contains(entries, x => x.Title == "甲頁面");
        Assert.Contains(entries, x => x.Title == "乙頁面");
        Assert.All(entries, x => Assert.Null(x.Url));
        Assert.All(entries, x => Assert.False(x.IsExternal));
    }

    [PostgresFact]
    public async Task Navigation_items_are_isolated_per_site()
    {
        var site = await CreateSiteAsync("navsite");
        using var admin = await App.AdministratorAsync(site.Id);
        var navigation = admin.Services.GetRequiredService<NavigationService>();
        await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "第二站項目", Url = "https://example.com/site2"
        });

        using var defaultAdmin = await App.AdministratorAsync();
        var defaultNavigation = defaultAdmin.Services.GetRequiredService<NavigationService>();
        Assert.Empty(await defaultNavigation.ListAsync());
    }

    [PostgresFact]
    public async Task Navigation_survives_export_and_import()
    {
        using var admin = await App.AdministratorAsync();
        var pageId = await CreatePageAsync("匯出頁面", "exported");
        await EnableCustomNavigationAsync();
        var navigation = admin.Services.GetRequiredService<NavigationService>();
        await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "匯出頁面", ContentId = pageId });
        await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "外部連結", Url = "https://example.com/" });

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        using var input = new MemoryStream(buffer.ToArray());
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        await using var db = await App.CreateContextAsync();
        var items = await db.SiteNavItems.AsNoTracking().OrderBy(x => x.Position).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal("匯出頁面", items[0].Title);
        Assert.NotNull(items[0].ContentId);
        Assert.Equal("外部連結", items[1].Title);
        Assert.Equal("https://example.com/", items[1].Url);
        Assert.True((await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1)).CustomNavigationEnabled);
    }
}
