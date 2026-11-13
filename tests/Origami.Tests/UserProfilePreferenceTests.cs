using Microsoft.Extensions.DependencyInjection;
using Origami.Core.Models;
using Origami.Core.Services;
using SkiaSharp;

namespace Origami.Tests;

/// <summary>
/// A 批「編輯器與個人化細節」：每使用者介面語言偏好與本機頭像（媒體庫選）。
/// 涵蓋偏好正規化與驗證、本機頭像優先序與前台投影、媒體刪除的頭像引用保護、
/// 個資匯出與抹除納入新欄位。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserProfilePreferenceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Ui_language_preference_normalizes_clears_and_rejects_unknown_keys()
    {
        using var admin = await App.AdministratorAsync();
        // 大小寫差異正規化為支援鍵。
        var updated = await admin.Sites.UpdateMyProfileAsync(new AccountProfileEdit { UiLanguage = "EN" });
        Assert.Equal("en", updated.UiLanguage);

        // 空字串代表清除偏好（跟隨訪客語言機制）。
        var cleared = await admin.Sites.UpdateMyProfileAsync(new AccountProfileEdit());
        Assert.Equal("", cleared.UiLanguage);

        // 不支援的語言鍵整體拒絕。
        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Sites.UpdateMyProfileAsync(new AccountProfileEdit { UiLanguage = "fr-FR" }));
    }

    [PostgresFact]
    public async Task Local_avatar_from_media_library_takes_precedence_and_blocks_media_deletion()
    {
        using var admin = await App.AdministratorAsync();
        var media = admin.Services.GetRequiredService<MediaService>();
        MediaItem image;
        await using (var stream = new MemoryStream(Png(64, 64)))
        {
            image = await media.UploadAsync(stream, "avatar.png");
        }
        await admin.Content.SaveAsync(NewContent("頭像作者的文章", status: ContentStatus.Published));

        var updated = await admin.Sites.UpdateMyProfileAsync(new AccountProfileEdit
        {
            DisplayName = "頭像作者",
            AvatarUrl = "https://example.com/legacy-avatar.png",
            AvatarMediaId = image.Id
        });
        Assert.Equal(image.Id, updated.AvatarMediaId);
        Assert.Equal("/media/" + image.FileName, updated.AvatarMediaUrl);

        // 作者彙整頁投影：本機頭像優先於外部網址。
        var author = await admin.Content.GetPublicAuthorAsync("頭像作者");
        Assert.NotNull(author);
        Assert.Equal("/media/" + image.FileName, author.AvatarUrl);

        // 媒體仍被頭像引用時刪除被拒。
        await Assert.ThrowsAsync<CmsException>(() => media.DeleteAsync(image.Id, confirmed: true));

        // 清除頭像後即可刪除。
        await admin.Sites.UpdateMyProfileAsync(new AccountProfileEdit { DisplayName = "頭像作者" });
        await media.DeleteAsync(image.Id, confirmed: true);
        var afterDelete = await admin.Sites.GetMyProfileAsync();
        Assert.Null(afterDelete.AvatarMediaId);
        Assert.Equal("", afterDelete.AvatarMediaUrl);
    }

    [PostgresFact]
    public async Task Avatar_media_must_reference_existing_image_in_media_library()
    {
        using var admin = await App.AdministratorAsync();
        // 不存在的媒體資產。
        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Sites.UpdateMyProfileAsync(new AccountProfileEdit { AvatarMediaId = Guid.NewGuid() }));

        // 非圖片資產不得作為頭像。
        var media = admin.Services.GetRequiredService<MediaService>();
        MediaItem document;
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A };
        await using (var stream = new MemoryStream(pdf))
        {
            document = await media.UploadFileAsync(stream, "doc.pdf");
        }
        Assert.Equal(MediaKind.Document, document.Kind);
        await Assert.ThrowsAsync<CmsException>(() =>
            admin.Sites.UpdateMyProfileAsync(new AccountProfileEdit { AvatarMediaId = document.Id }));
    }

    [PostgresFact]
    public async Task Personal_data_export_and_erase_include_new_preference_fields()
    {
        var password = IsolatedPostgres.NewPassword();
        using var admin = await App.AdministratorAsync();
        var user = await admin.Sites.CreateUserAsync(new UserCreateInput { UserName = "writer-lang", Password = password });

        var media = admin.Services.GetRequiredService<MediaService>();
        MediaItem image;
        await using (var stream = new MemoryStream(Png(48, 48)))
        {
            image = await media.UploadAsync(stream, "writer-avatar.png");
        }

        using (var actor = await App.UserAsync(user.Id))
        {
            await actor.Sites.UpdateMyProfileAsync(new AccountProfileEdit
            {
                DisplayName = "多語作者",
                UiLanguage = "en",
                AvatarMediaId = image.Id
            });
            var export = await actor.Sites.ExportMyPersonalDataAsync();
            Assert.Equal("en", export.UiLanguage);
            Assert.Equal(image.Id, export.AvatarMediaId);
        }

        // 抹除後新欄位一併清空，媒體引用解除後可刪除。
        await admin.Sites.EraseUserPersonalDataAsync(user.Id);
        Assert.Null((await admin.Sites.GetMyProfileAsync()).AvatarMediaId);
        await media.DeleteAsync(image.Id, confirmed: true);
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90)!;
        return encoded.ToArray();
    }
}
