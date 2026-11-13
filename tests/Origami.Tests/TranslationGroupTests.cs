using Origami.Core.Models;
using Origami.Core.Services;

namespace Origami.Tests;

/// <summary>
/// C 批「一稿多譯」：平權翻譯組（TranslationGroupId）與內容語言標記的服務層邊界。
/// 僅翻譯關聯，不做自動翻譯；前台切換與列表行為另見後續測試。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TranslationGroupTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Linking_two_contents_creates_group_and_same_language_is_rejected()
    {
        using var admin = await App.AdministratorAsync();
        var zh = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "中文原文", Language = "zh-Hant", Status = ContentStatus.Published
        });
        Assert.Null(zh.TranslationGroupId);

        var en = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "English version", Language = "en", Status = ContentStatus.Published,
            LinkTranslationTo = zh.Id
        });
        // 目標原本無組：關聯後兩者同屬一個新組。
        Assert.NotNull(en.TranslationGroupId);
        Assert.NotEqual(zh.Id, en.TranslationGroupId!.Value);
        var zhReloaded = await admin.Content.GetEditAsync(zh.Id!.Value);
        Assert.Equal(en.TranslationGroupId, zhReloaded.TranslationGroupId);

        // 同組同語言的第二個譯本一律拒絕，避免前台語言切換出現二義。
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "Another English", Language = "en", Status = ContentStatus.Draft,
            LinkTranslationTo = zh.Id
        }));

        // 關聯第三種語言成功，組繼續擴大。
        var ja = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "日本語版", Language = "ja", Status = ContentStatus.Draft,
            LinkTranslationTo = en.Id
        });
        Assert.Equal(en.TranslationGroupId, ja.TranslationGroupId);
    }

    [PostgresFact]
    public async Task Translation_group_validates_kind_self_and_language_key()
    {
        using var admin = await App.AdministratorAsync();
        var post = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "文章", Language = "zh-Hant", Status = ContentStatus.Published
        });
        var page = await admin.Content.CreateAsync(ContentKind.Page, new ContentEdit
        {
            Title = "頁面", Language = "zh-Hant", Status = ContentStatus.Published
        });

        // 跨類型不可同組。
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(new ContentEdit
        {
            Id = page.Id, Kind = ContentKind.Page, Title = "頁面", Slug = page.Slug, Status = ContentStatus.Published,
            LinkTranslationTo = post.Id
        }));

        // 不可指向自身；語言鍵格式錯誤與互斥操作也一律拒絕。
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(new ContentEdit
        {
            Id = post.Id, Kind = ContentKind.Post, Title = "文章", Slug = post.Slug, Status = ContentStatus.Published,
            LinkTranslationTo = post.Id
        }));
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(new ContentEdit
        {
            Id = post.Id, Kind = ContentKind.Post, Title = "文章", Slug = post.Slug, Status = ContentStatus.Published,
            Language = "not a lang!"
        }));
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(new ContentEdit
        {
            Id = post.Id, Kind = ContentKind.Post, Title = "文章", Slug = post.Slug, Status = ContentStatus.Published,
            LinkTranslationTo = page.Id, LeaveTranslationGroup = true
        }));
    }

    [PostgresFact]
    public async Task Leaving_group_does_not_affect_other_members()
    {
        using var admin = await App.AdministratorAsync();
        var zh = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "中文", Language = "zh-Hant", Status = ContentStatus.Published
        });
        var en = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "English", Language = "en", Status = ContentStatus.Published,
            LinkTranslationTo = zh.Id
        });

        var leaveEdit = await admin.Content.GetEditAsync(en.Id!.Value);
        leaveEdit.LeaveTranslationGroup = true;
        var left = await admin.Content.UpdateAsync(leaveEdit);
        Assert.Null(left.TranslationGroupId);
        // 原文保持在組內（組不成為空殼也不強制清理）。
        var zhReloaded = await admin.Content.GetEditAsync(zh.Id!.Value);
        Assert.NotNull(zhReloaded.TranslationGroupId);

        // 離組後可用原語言重新與其他內容組隊。
        var relinkEdit = await admin.Content.GetEditAsync(en.Id.Value);
        relinkEdit.LinkTranslationTo = zh.Id;
        var again = await admin.Content.UpdateAsync(relinkEdit);
        Assert.NotNull(again.TranslationGroupId);
    }

    [PostgresFact]
    public async Task Public_detail_switches_translations_and_falls_back_with_notice()
    {
        using var admin = await App.AdministratorAsync();
        var zh = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "中文版", Language = "zh-Hant", Status = ContentStatus.Published
        });
        var en = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "English Edition", Language = "en", Status = ContentStatus.Published,
            LinkTranslationTo = zh.Id
        });

        // 以原文代稱 + en 切換到英譯；譯本清單互相指向對方。
        var inEnglish = await admin.Content.GetPublishedAsync(ContentKind.Post, zh.Slug!, "en");
        Assert.NotNull(inEnglish);
        Assert.Equal("English Edition", inEnglish!.Title);
        Assert.Equal("en", inEnglish.Language);
        Assert.Null(inEnglish.FallbackFromLanguage);
        Assert.Contains(inEnglish.AvailableTranslations, x => x.Slug == zh.Slug);

        // 請求的語言無譯本：回退顯示原內容並帶提示旗標。
        var fallback = await admin.Content.GetPublishedAsync(ContentKind.Post, zh.Slug!, "ja");
        Assert.NotNull(fallback);
        Assert.Equal("中文版", fallback!.Title);
        Assert.Equal("ja", fallback.FallbackFromLanguage);

        // 撤回的譯本不再被切換命中（與公開路由 404 同步規則）。
        var withdrawn = await admin.Content.GetEditAsync(en.Id!.Value);
        withdrawn.Status = ContentStatus.Draft;
        await admin.Content.UpdateAsync(withdrawn);
        var afterWithdraw = await admin.Content.GetPublishedAsync(ContentKind.Post, zh.Slug!, "en");
        Assert.NotNull(afterWithdraw);
        Assert.Equal("中文版", afterWithdraw!.Title);
        Assert.Equal("en", afterWithdraw.FallbackFromLanguage);
        Assert.DoesNotContain(afterWithdraw.AvailableTranslations, x => x.Slug == en.Slug);
    }

    [PostgresFact]
    public async Task Public_list_shows_one_entry_per_translation_group_following_language()
    {
        using var admin = await App.AdministratorAsync();
        var zh = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "組內中文", Language = "zh-Hant", Status = ContentStatus.Published
        });
        var en = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "Group English", Language = "en", Status = ContentStatus.Published,
            LinkTranslationTo = zh.Id
        });
        var standalone = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "獨立文章", Status = ContentStatus.Published
        });

        // 預設（無語言）：組代表為站點預設語言的譯本。
        var byDefault = await admin.Content.ListPublishedAsync();
        Assert.Equal(2, byDefault.Total);
        Assert.Contains(byDefault.Items, x => x.Title == "組內中文");
        Assert.DoesNotContain(byDefault.Items, x => x.Title == "Group English");
        Assert.Contains(byDefault.Items, x => x.Title == "獨立文章");

        // ?lang=en：同一組改列英譯，數量不變。
        var inEnglish = await admin.Content.ListPublishedAsync(language: "en");
        Assert.Equal(2, inEnglish.Total);
        Assert.Contains(inEnglish.Items, x => x.Title == "Group English");
        Assert.DoesNotContain(inEnglish.Items, x => x.Title == "組內中文");

        // 無譯本語言：回退到站點預設語言代表。
        var inJapanese = await admin.Content.ListPublishedAsync(language: "ja");
        Assert.Equal(2, inJapanese.Total);
        Assert.Contains(inJapanese.Items, x => x.Title == "組內中文");
    }
}
