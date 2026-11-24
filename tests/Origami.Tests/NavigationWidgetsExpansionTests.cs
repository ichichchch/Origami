namespace Origami.Tests;

/// <summary>多層選單（上層、位置、新目標類型）與六種新小工具。</summary>
[Collection(PostgresCollection.Name)]
public sealed class NavigationWidgetsExpansionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static NavigationService Navigation(TestActor actor) => actor.Services.GetRequiredService<NavigationService>();
    private static WidgetService Widgets(TestActor actor) => actor.Services.GetRequiredService<WidgetService>();

    [PostgresFact]
    public async Task Navigation_supports_nesting_footer_location_and_new_target_kinds()
    {
        using var admin = await App.AdministratorAsync();
        var navigation = Navigation(admin);
        var page = await admin.Content.SaveAsync(NewContent("關於我們", kind: ContentKind.Page, status: ContentStatus.Published));
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "生活" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "週記" });

        var parent = await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "關於", ContentId = page.Id });
        var child = await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "生活隨筆", CategoryId = category.Id, ParentId = parent.Id
        });
        var tagItem = await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "每週", TagId = tag.Id });
        var archives = await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "歸檔", ToArchives = true });
        var footer = await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "外部社群", Url = "https://example.com/social", Location = 1
        });

        // 目標必須恰好一個；導覽最多兩層；不可把自己設為上層。
        await Assert.ThrowsAsync<CmsException>(() => navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "雙目標", ContentId = page.Id, Url = "https://example.com", ToArchives = true
        }));
        await Assert.ThrowsAsync<CmsException>(() => navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "孫項目", ToArchives = true, ParentId = child.Id
        }));
        await Assert.ThrowsAsync<CmsException>(() => navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Id = parent.Id, Version = parent.Version, Title = "關於", ContentId = page.Id, ParentId = parent.Id
        }));
        await Assert.ThrowsAsync<CmsException>(() => navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "不存在的作者", AuthorSlug = "nobody-here"
        }));

        var settings = await admin.Settings.GetAsync();
        settings.CustomNavigationEnabled = true;
        await admin.Settings.SaveAsync(settings);

        var publicItems = await navigation.PublicAsync();
        var publicChild = publicItems.Single(x => x.Id == child.Id);
        Assert.Equal(parent.Id, publicChild.ParentId);
        Assert.Equal("/category/" + Uri.EscapeDataString(category.Slug ?? ""), publicChild.Url);
        Assert.Equal("/tag/" + Uri.EscapeDataString(tag.Slug ?? ""), publicItems.Single(x => x.Id == tagItem.Id).Url);
        Assert.Equal("/archives", publicItems.Single(x => x.Id == archives.Id).Url);
        var publicFooter = publicItems.Single(x => x.Id == footer.Id);
        Assert.True(publicFooter.IsFooter);
        Assert.True(publicFooter.IsExternal);
        Assert.Equal("/pages/" + Uri.EscapeDataString(page.Slug ?? ""), publicItems.Single(x => x.Id == parent.Id).Url);

        // 刪除上層項目後，子項目上移為最上層。
        await navigation.DeleteAsync(parent.Id, parent.Version);
        Assert.Null((await navigation.ListAsync()).Single(x => x.Id == child.Id).ParentId);
        Assert.True((await navigation.PublicAsync()).Single(x => x.Id == child.Id).ParentId is null);
    }

    [PostgresFact]
    public async Task New_widget_types_validate_media_and_render_public_data()
    {
        using var admin = await App.AdministratorAsync();
        var widgets = Widgets(admin);
        var navigation = Navigation(admin);
        var image = "/media/" + new string('a', 32) + ".png";
        var audio = "/media/" + new string('b', 32) + ".mp3";
        await admin.Content.SaveAsync(NewContent("月份文章", status: ContentStatus.Published));
        var page = await admin.Content.SaveAsync(NewContent("公開頁面", kind: ContentKind.Page, status: ContentStatus.Published));
        var hidden = NewContent("私密頁面", kind: ContentKind.Page, status: ContentStatus.Published);
        hidden.IsPrivate = true;
        await admin.Content.SaveAsync(hidden);
        var settings = await admin.Settings.GetAsync();
        settings.SocialXUrl = "https://x.example.com/site";
        settings.CustomNavigationEnabled = true;
        await admin.Settings.SaveAsync(settings);
        await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "頁面", ContentId = page.Id });

        // 媒體只接受站內 /media/；圖片限點陣圖，音訊／影片限可播放副檔名；月數上限 24。
        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Sidebar, Type = WidgetType.Image, Url = "https://evil.example/a.png" }));
        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Sidebar, Type = WidgetType.AudioVideo, Url = "/media/" + new string('c', 32) + ".txt" }));
        await Assert.ThrowsAsync<CmsException>(() => widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Sidebar, Type = WidgetType.MonthlyArchives, Count = 30 }));

        foreach (var type in new[] { WidgetType.PageList, WidgetType.MonthlyArchives, WidgetType.Image, WidgetType.AudioVideo,
                     WidgetType.NavigationMenu, WidgetType.SocialLinks })
        {
            await widgets.SaveAsync(new WidgetService.WidgetInput
            {
                Area = type is WidgetType.PageList or WidgetType.NavigationMenu ? WidgetArea.Sidebar : WidgetArea.Footer, Type = type,
                Count = type == WidgetType.MonthlyArchives ? 6 : null,
                Url = type == WidgetType.Image ? image : type == WidgetType.AudioVideo ? audio : null,
                Markdown = type is WidgetType.Image or WidgetType.AudioVideo ? "說明文字" : null
            });
        }

        var publicWidgets = await widgets.PublicAsync();
        Assert.Equal(6, publicWidgets.Count);
        var pageList = publicWidgets.Single(x => x.Type == WidgetType.PageList);
        Assert.Equal("公開頁面", Assert.Single(pageList.Pages).Title);
        var months = publicWidgets.Single(x => x.Type == WidgetType.MonthlyArchives);
        Assert.Equal(DateTimeOffset.UtcNow.Month, Assert.Single(months.ArchiveMonths).Month);
        var imageWidget = publicWidgets.Single(x => x.Type == WidgetType.Image);
        Assert.Equal(image, imageWidget.MediaUrl);
        Assert.False(imageWidget.IsAudioVideo);
        var audioWidget = publicWidgets.Single(x => x.Type == WidgetType.AudioVideo);
        Assert.Equal(audio, audioWidget.MediaUrl);
        Assert.True(audioWidget.IsAudioVideo);
        Assert.Equal("https://x.example.com/site", Assert.Single(publicWidgets.Single(x => x.Type == WidgetType.SocialLinks).SocialLinks).Url);
        Assert.Equal("頁面", Assert.Single(publicWidgets.Single(x => x.Type == WidgetType.NavigationMenu).MenuItems).Name);
    }

    [PostgresFact]
    public async Task Nested_navigation_and_new_widgets_render_in_their_locations_over_http()
    {
        using var admin = await App.AdministratorAsync();
        var widgets = Widgets(admin);
        var navigation = Navigation(admin);
        var page = await admin.Content.SaveAsync(NewContent("公開獨立頁", kind: ContentKind.Page, status: ContentStatus.Published));
        await admin.Content.SaveAsync(NewContent("本月文章", status: ContentStatus.Published));
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "生活" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new TaxonomyEdit { Name = "AAA 標籤" });
        var parent = await navigation.SaveAsync(new NavigationService.NavItemInput { Title = "關於我們", ContentId = page.Id });
        await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "生活隨筆", CategoryId = category.Id, ParentId = parent.Id
        });
        await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "AAA 子項目", TagId = tag.Id, ParentId = parent.Id
        });
        await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "Z 頁尾外連", Url = "https://example.com/footer", Location = 1
        });
        await navigation.SaveAsync(new NavigationService.NavItemInput
        {
            Title = "A 頁尾歸檔", ToArchives = true, Location = 1
        });
        var settings = await admin.Settings.GetAsync();
        settings.CustomNavigationEnabled = true;
        settings.SocialXUrl = "https://x.example.com/site";
        await admin.Settings.SaveAsync(settings);

        var imageName = new string('a', 32) + ".png";
        var audioName = new string('b', 32) + ".wav";
        var videoUrl = "/media/" + new string('c', 32) + ".mp4";
        await using var site = new RunningSite(App.ConnectionString);
        var images = Path.Combine(site.StorageRoot, "images");
        Directory.CreateDirectory(images);
        using (var bitmap = new SKBitmap(new SKImageInfo(320, 180)))
        {
            bitmap.Erase(SKColors.SteelBlue);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 90)!;
            await File.WriteAllBytesAsync(Path.Combine(images, imageName), encoded.ToArray());
        }
        await File.WriteAllBytesAsync(Path.Combine(images, audioName), Wav());

        await widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Sidebar, Type = WidgetType.PageList, Title = "頁面小工具"
        });
        await widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Sidebar, Type = WidgetType.MonthlyArchives, Title = "月份小工具", Count = 6
        });
        await widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Sidebar, Type = WidgetType.NavigationMenu, Title = "選單小工具"
        });
        await widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Footer, Type = WidgetType.Image, Title = "圖片小工具",
            Url = "/media/" + imageName, Markdown = "圖片說明"
        });
        await widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Footer, Type = WidgetType.AudioVideo, Title = "音訊小工具",
            Url = "/media/" + audioName, Markdown = "音訊說明"
        });
        await widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Footer, Type = WidgetType.AudioVideo, Title = "影片小工具",
            Url = videoUrl, Markdown = "影片說明"
        });
        await widgets.SaveAsync(new WidgetService.WidgetInput
        {
            Area = WidgetArea.Footer, Type = WidgetType.SocialLinks, Title = "社群小工具"
        });

        await site.StartAsync(environment: "Production");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge", Headless = true
        });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var browserPage = await context.NewPageAsync();
        var pageErrors = new ConcurrentQueue<string>();
        browserPage.PageError += (_, error) => pageErrors.Enqueue(error);
        await browserPage.GotoAsync("/");
        await browserPage.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(browserPage.Locator(".site-nav")).ToContainTextAsync("關於我們");
        await Expect(browserPage.Locator(".site-nav .nav-child").First).ToContainTextAsync("生活隨筆");
        await Expect(browserPage.Locator(".site-nav .nav-child").Nth(1)).ToContainTextAsync("AAA 子項目");
        await Expect(browserPage.Locator(".site-nav")).Not.ToContainTextAsync("頁尾歸檔");
        await Expect(browserPage.Locator(".footer-nav a").First)
            .ToHaveAttributeAsync("href", "https://example.com/footer");
        await Expect(browserPage.Locator(".footer-nav a").Nth(1))
            .ToHaveAttributeAsync("href", "/archives");
        await Expect(browserPage.Locator(".site-sidebar .widget-pagelist")).ToContainTextAsync("公開獨立頁");
        await Expect(browserPage.Locator(".site-sidebar .widget-pagelist a"))
            .ToHaveAttributeAsync("href", "/pages/" + Uri.EscapeDataString(page.Slug ?? ""));
        await Expect(browserPage.Locator(".site-sidebar .widget-navigationmenu")).ToContainTextAsync("生活隨筆");
        var menuLinks = browserPage.Locator(".site-sidebar .widget-navigationmenu a");
        await Expect(menuLinks.First)
            .ToHaveAttributeAsync("href", "/pages/" + Uri.EscapeDataString(page.Slug ?? ""));
        await Expect(menuLinks.Nth(1))
            .ToHaveAttributeAsync("href", "/category/" + Uri.EscapeDataString(category.Slug ?? ""));
        await Expect(menuLinks.Nth(2))
            .ToHaveAttributeAsync("href", "/tag/" + Uri.EscapeDataString(tag.Slug ?? ""));
        var now = DateTimeOffset.UtcNow;
        await Expect(browserPage.Locator(".widget-monthlyarchives a").First)
            .ToHaveAttributeAsync("href", $"/archives/{now.Year}/{now.Month}");
        await Expect(browserPage.Locator(".footer-widgets img.widget-image"))
            .ToHaveAttributeAsync("src", "/media/" + imageName);
        await Expect(browserPage.Locator(".footer-widgets .widget-audiovideo audio"))
            .ToHaveAttributeAsync("src", "/media/" + audioName);
        await Expect(browserPage.Locator(".footer-widgets video"))
            .ToHaveAttributeAsync("src", videoUrl);
        await Expect(browserPage.Locator(".footer-widgets .widget-sociallinks a"))
            .ToHaveAttributeAsync("href", "https://x.example.com/site");

        await browserPage.Locator(".site-nav .nav-child").Filter(new() { HasText = "生活隨筆" }).ClickAsync();
        await browserPage.WaitForURLAsync("**/category/" + Uri.EscapeDataString(category.Slug ?? ""));
        Assert.Empty(pageErrors);
    }

    private static byte[] Wav()
    {
        const int sampleRate = 8000;
        const int sampleCount = 800;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + sampleCount * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(sampleCount * 2);
        for (var i = 0; i < sampleCount; i++) writer.Write((short)0);
        writer.Flush();
        return stream.ToArray();
    }
}
