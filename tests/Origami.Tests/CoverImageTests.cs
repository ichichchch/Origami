namespace Origami.Tests;

/// <summary>封面圖（Featured Image）與媒體說明文字（caption）的閉環測試。</summary>
[Collection(PostgresCollection.Name)]
public sealed class CoverImageTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Token = "isolated-test-service-token-0123456789abcdef";

    /// <summary>直接建立媒體資產列；封面與 meta 測試只需 URL 字串，不依賴磁碟檔案。</summary>
    private async Task<MediaAsset> CreateAssetAsync(string altText = "封面替代文字", string caption = "封面說明")
    {
        await using var db = await App.CreateContextAsync();
        var asset = new MediaAsset
        {
            FileName = Guid.NewGuid().ToString("N") + ".png", OriginalName = "cover.png",
            ContentType = "image/png", AltText = altText, Caption = caption,
            Width = 1200, Height = 400, UploadedAt = DateTimeOffset.UtcNow
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    /// <summary>指向暫存目錄的媒體服務；容器註冊的 StoragePaths 指向實際應用目錄，測試不可寫入。</summary>
    private static MediaService CreateMediaService(TestActor actor, TemporaryMediaStorage directory) => new(
        actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
        actor.Services.GetRequiredService<AdminAccess>(), directory.Create(), TimeProvider.System, NullLogger<MediaService>.Instance);

    [PostgresFact]
    public async Task Cover_round_trips_through_edit_save_and_public_reads()
    {
        using var admin = await App.AdministratorAsync();
        var asset = await CreateAssetAsync();

        var post = NewContent("有封面的文章");
        post.CoverMediaId = asset.Id;
        await admin.Content.SaveAsync(post);
        var edit = await admin.Content.GetEditAsync(post.Id!.Value);
        Assert.Equal(asset.Id, edit.CoverMediaId);
        Assert.Equal(asset.FileName, edit.CoverFileName);
        Assert.Equal("封面替代文字", edit.CoverAltText);
        Assert.Equal("封面說明", edit.CoverCaption);

        // 發布後的公開列表與詳情都帶封面；詳情另含說明文字。
        edit.Status = ContentStatus.Published;
        await admin.Content.UpdateAsync(edit);
        var summary = (await admin.Content.ListPublishedAsync()).Items.Single(x => x.Id == post.Id);
        Assert.Equal("/media/" + asset.FileName, summary.CoverUrl);
        var detail = await admin.Content.GetPublishedAsync(ContentKind.Post, edit.Slug!);
        Assert.NotNull(detail);
        Assert.Equal("/media/" + asset.FileName, detail!.CoverUrl);
        Assert.Equal("封面說明", detail.CoverCaption);

        // 明確送出 null 可清除封面；未設定時 CoverUrl 為 null。
        var cleared = await admin.Content.GetEditAsync(post.Id.Value);
        cleared.CoverMediaId = null;
        await admin.Content.UpdateAsync(cleared);
        Assert.Null((await admin.Content.GetEditAsync(post.Id.Value)).CoverMediaId);
        Assert.Null((await admin.Content.GetPublishedAsync(ContentKind.Post, edit.Slug!))!.CoverUrl);
    }

    [PostgresFact]
    public async Task Cover_and_caption_inputs_are_validated()
    {
        using var admin = await App.AdministratorAsync();
        var asset = await CreateAssetAsync();
        using var mediaDirectory = new TemporaryMediaStorage();
        var media = CreateMediaService(admin, mediaDirectory);

        var bad = NewContent("假封面");
        bad.CoverMediaId = Guid.NewGuid();
        var refused = await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(bad));
        Assert.Contains("封面圖片不存在", refused.Message, StringComparison.Ordinal);

        var trimmed = await media.UpdateMetaAsync(asset.Id, "  alt  ", "  說明  ");
        Assert.Equal("alt", trimmed.AltText);
        Assert.Equal("說明", trimmed.Caption);
        var tooLong = await Assert.ThrowsAsync<CmsException>(() =>
            media.UpdateMetaAsync(asset.Id, null, new string('長', Limits.Caption + 1)));
        Assert.Contains("說明文字", tooLong.Message, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Cover_references_block_media_deletion_until_cleared()
    {
        using var admin = await App.AdministratorAsync();
        var asset = await CreateAssetAsync();
        using var mediaDirectory = new TemporaryMediaStorage();
        var media = CreateMediaService(admin, mediaDirectory);
        Assert.Equal(0, await media.CountReferencesAsync(asset.Id));

        // 僅以封面引用（本文不內嵌），刪除保護必須同樣成立。
        var post = NewContent("封面引用文章");
        post.CoverMediaId = asset.Id;
        await admin.Content.SaveAsync(post);
        Assert.Equal(1, await media.CountReferencesAsync(asset.Id));
        Assert.Equal("封面引用文章", (await media.ListReferencesAsync(asset.Id)).Single().Title);
        var refused = await Assert.ThrowsAsync<CmsException>(() => media.DeleteAsync(asset.Id, true));
        Assert.Contains("封面引用文章", refused.Message, StringComparison.Ordinal);

        var edit = await admin.Content.GetEditAsync(post.Id!.Value);
        edit.CoverMediaId = null;
        await admin.Content.UpdateAsync(edit);
        Assert.Equal(0, await media.CountReferencesAsync(asset.Id));
        await media.DeleteAsync(asset.Id, true);
    }

    [PostgresFact]
    public async Task Admin_api_round_trips_cover_and_public_api_exposes_it()
    {
        using var actor = await App.AdministratorAsync();
        var asset = await CreateAssetAsync();
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var adminClient = api.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        // BFF 合約：管理端點要求服務通行證＋有效的管理員工作階段戳記。
        adminClient.DefaultRequestHeaders.Add(AdminSession.StampHeader, await App.AdminSecurityStampAsync());
        adminClient.DefaultRequestHeaders.Add(AccessClaims.UserHeader, await App.AdminUserIdAsync());

        using var created = await adminClient.PostAsJsonAsync("/api/v1/admin/contents",
            new { title = "API 封面文章", kind = "Post", markdown = "本文", status = "Published", coverMediaId = asset.Id });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        var id = (Guid)body["id"]!;
        Assert.Equal(asset.FileName, (string)body["coverFileName"]!);

        using var publicClient = api.CreateClient();
        using var publicDetail = await publicClient.GetAsync("/api/v1/posts/" + (string)body["slug"]!);
        Assert.Equal(HttpStatusCode.OK, publicDetail.StatusCode);
        var detail = JsonNode.Parse(await publicDetail.Content.ReadAsStringAsync())!;
        Assert.Equal("/media/" + asset.FileName, (string)detail["coverUrl"]!);
        Assert.Equal("封面說明", (string)detail["coverCaption"]!);

        // 媒體 meta 端點更新說明文字；公開詳情隨之反映。
        using var meta = await adminClient.PutAsJsonAsync($"/api/v1/admin/media/{asset.Id}/meta",
            new { altText = "新替代文字", caption = "新說明" });
        Assert.True(meta.IsSuccessStatusCode, $"media meta 更新失敗：{(int)meta.StatusCode} | {string.Join(" || ", api.ConsoleErrors)}");
        using var refreshed = await publicClient.GetAsync("/api/v1/posts/" + (string)body["slug"]!);
        Assert.Equal("新說明", (string)JsonNode.Parse(await refreshed.Content.ReadAsStringAsync())!["coverCaption"]!);

        // 封面引用使刪除被拒；訊息指出引用內容。
        using var blocked = await adminClient.DeleteAsync($"/api/v1/admin/media/{asset.Id}?confirmed=true");
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("API 封面文章",
            (string)JsonNode.Parse(await blocked.Content.ReadAsStringAsync())!["message"]!, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Published_cover_renders_with_caption_and_og_image_on_the_site()
    {
        using var admin = await App.AdministratorAsync();
        var asset = await CreateAssetAsync(altText: "站內封面替代文字", caption: "站內封面說明");
        var post = NewContent("網站封面文章", status: ContentStatus.Published);
        post.CoverMediaId = asset.Id;
        await admin.Content.SaveAsync(post);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        using var response = await client.GetAsync("/posts/" + post.Slug);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("cover-figure", html, StringComparison.Ordinal);
        Assert.Contains("/media/" + asset.FileName, html, StringComparison.Ordinal);
        Assert.Contains("srcset=", html, StringComparison.Ordinal);
        Assert.Contains("站內封面說明", html, StringComparison.Ordinal);
        Assert.Contains("og:image", html, StringComparison.Ordinal);
        Assert.Contains("og:image:alt", html, StringComparison.Ordinal);
        Assert.Contains("站內封面替代文字", html, StringComparison.Ordinal);

        // 列表卡片帶封面縮圖（首頁與 /posts 共用同一元件）。
        using var home = await client.GetAsync("/");
        var homeHtml = await home.Content.ReadAsStringAsync();
        Assert.Contains("post-cover", homeHtml, StringComparison.Ordinal);
        Assert.Contains("/media/" + asset.FileName, homeHtml, StringComparison.Ordinal);
    }
}
