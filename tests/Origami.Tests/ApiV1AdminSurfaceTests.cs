namespace Origami.Tests;

/// <summary>第二批 BFF 介面：分類標籤、媒體、設定、修訂版本／預覽、匯出／匯入。</summary>
[Collection(PostgresCollection.Name)]
public sealed class ApiV1AdminSurfaceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Token = "isolated-test-service-token-0123456789abcdef";

    private async Task<HttpClient> CreateAdminClientAsync(RunningApi api, string stamp)
    {
        return api.CreateBffClient(Token, await App.AdminUserIdAsync(), stamp);
    }

    private async Task<RunningApi> StartApiAsync()
    {
        var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        return api;
    }

    [PostgresFact]
    public async Task Taxonomies_round_trip_over_the_admin_api()
    {
        using var actor = await App.AdministratorAsync();
        await using var api = await StartApiAsync();
        using var admin = await CreateAdminClientAsync(api, await App.AdminSecurityStampAsync());

        using var created = await admin.PostAsJsonAsync("/api/v1/admin/taxonomies?kind=category", new { name = "分類甲" });
        Assert.True(created.StatusCode == HttpStatusCode.OK, "建立分類失敗：" + await created.Content.ReadAsStringAsync());
        var category = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        Assert.NotEqual(Guid.Empty, (Guid)category["id"]!);

        using var listed = await admin.GetAsync("/api/v1/admin/taxonomies?kind=category");
        var list = JsonNode.Parse(await listed.Content.ReadAsStringAsync())!;
        Assert.Contains(list["items"]!.AsArray(), item => item!["name"]!.GetValue<string>() == "分類甲");

        using var renamed = await admin.PutAsJsonAsync($"/api/v1/admin/taxonomies/{(Guid)category["id"]!}?kind=category",
            new { id = (Guid)category["id"]!, name = "分類乙", slug = (string)category["slug"]!, version = (string)category["version"]! });
        Assert.True(renamed.StatusCode == HttpStatusCode.OK, "改名失敗：" + await renamed.Content.ReadAsStringAsync());

        // 重新取得輪替後的 version，再刪除。
        using var renamedBody = await admin.GetAsync("/api/v1/admin/taxonomies?kind=category");
        var fresh = JsonNode.Parse(await renamedBody.Content.ReadAsStringAsync())!["items"]!.AsArray()
            .Single(item => item!["name"]!.GetValue<string>() == "分類乙")!;
        using var realDelete = await admin.DeleteAsync($"/api/v1/admin/taxonomies/{(Guid)fresh["id"]!}?kind=category&version={fresh["version"]!}");
        Assert.Equal(HttpStatusCode.NoContent, realDelete.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/v1/admin/taxonomies?kind=wrong")).StatusCode);
    }

    [PostgresFact]
    public async Task Media_upload_alt_references_and_protected_delete()
    {
        using var actor = await App.AdministratorAsync();
        await using var api = await StartApiAsync();
        using var admin = await CreateAdminClientAsync(api, await App.AdminSecurityStampAsync());

        using var form = new MultipartFormDataContent();
        using var image = new ByteArrayContent(PngBytes());
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "file", "照片.png");
        using var uploaded = await admin.PostAsync("/api/v1/admin/media", form);
        Assert.True(uploaded.StatusCode == HttpStatusCode.Created,
            "上傳失敗：" + (int)uploaded.StatusCode + " " + await uploaded.Content.ReadAsStringAsync()
            + " | " + string.Join(" || ", api.ConsoleErrors));
        var media = JsonNode.Parse(await uploaded.Content.ReadAsStringAsync())!;
        var fileName = (string)media["fileName"]!;
        Assert.EndsWith(".png", fileName);

        using var alt = await admin.PutAsJsonAsync($"/api/v1/admin/media/{(Guid)media["id"]!}/meta", new { altText = "替代文字", caption = "說明文字" });
        Assert.Equal(HttpStatusCode.OK, alt.StatusCode);
        using var references = await admin.GetAsync($"/api/v1/admin/media/{(Guid)media["id"]!}/references");
        Assert.Equal(HttpStatusCode.OK, references.StatusCode);

        // 有內容引用時，媒體檔受保護不得刪除。
        using var post = await admin.PostAsJsonAsync("/api/v1/admin/contents",
            new { title = "引用文章", slug = "引用文章", kind = "Post", markdown = $"![圖](/media/{fileName})", status = "Published" });
        var postBody = JsonNode.Parse(await post.Content.ReadAsStringAsync())!;
        using var refused = await admin.DeleteAsync($"/api/v1/admin/media/{(Guid)media["id"]!}?confirmed=true");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var refusedMessage = await refused.Content.ReadAsStringAsync();
        Assert.Contains("引用", refusedMessage);

        // 引用內容移除後，媒體檔即可刪除。
        var contentId = (Guid)postBody["id"]!;
        using var trashed = await admin.DeleteAsync($"/api/v1/admin/contents/{contentId}?version={postBody["version"]!}");
        Assert.Equal(HttpStatusCode.NoContent, trashed.StatusCode);
        var versionAfterTrash = await VersionAfterTrash(admin, contentId);
        using var purge = await admin.DeleteAsync($"/api/v1/admin/contents/{contentId}?version={versionAfterTrash}&permanent=true");
        Assert.True(purge.StatusCode == HttpStatusCode.NoContent,
            $"永久刪除失敗（v={versionAfterTrash}）：" + await purge.Content.ReadAsStringAsync());
        using var deleted = await admin.DeleteAsync($"/api/v1/admin/media/{(Guid)media["id"]!}?confirmed=true");
        Assert.True(deleted.StatusCode == HttpStatusCode.NoContent, "刪除媒體失敗：" + await deleted.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task Settings_round_trip_without_touching_other_fields()
    {
        using var actor = await App.AdministratorAsync();
        await using var api = await StartApiAsync();
        using var admin = await CreateAdminClientAsync(api, await App.AdminSecurityStampAsync());

        using var before = await admin.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var settings = JsonNode.Parse(await before.Content.ReadAsStringAsync())!;
        settings["name"] = "批次二站名";
        using var saved = await admin.PutAsync("/api/v1/admin/settings",
            new StringContent(settings.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var after = await admin.GetAsync("/api/v1/admin/settings");
        Assert.Equal("批次二站名", (string)JsonNode.Parse(await after.Content.ReadAsStringAsync())!["name"]!);
    }

    [PostgresFact]
    public async Task Revisions_preview_tokens_and_counts()
    {
        using var actor = await App.AdministratorAsync();
        await using var api = await StartApiAsync();
        using var admin = await CreateAdminClientAsync(api, await App.AdminSecurityStampAsync());

        using var created = await admin.PostAsJsonAsync("/api/v1/admin/contents",
            new { title = "修訂文章", slug = "修訂文章", kind = "Post", markdown = "第一版", status = "Published" });
        var first = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;

        using var updated = await admin.PutAsJsonAsync($"/api/v1/admin/contents/{(Guid)first["id"]!}",
            new { title = "修訂文章", slug = "修訂文章", kind = "Post", markdown = "第二版", status = "Published", version = (string)first["version"]! });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var revisions = await admin.GetAsync($"/api/v1/admin/contents/{(Guid)first["id"]!}/revisions");
        var list = JsonNode.Parse(await revisions.Content.ReadAsStringAsync())!.AsArray();
        Assert.NotEmpty(list);
        using var detail = await admin.GetAsync($"/api/v1/admin/revisions/{(Guid)list[0]!["id"]!}");
        Assert.Equal("第一版", (string)JsonNode.Parse(await detail.Content.ReadAsStringAsync())!["markdown"]!);

        using var token = await admin.PostAsync($"/api/v1/admin/contents/{(Guid)first["id"]!}/preview-token", null);
        var tokenBody = JsonNode.Parse(await token.Content.ReadAsStringAsync())!;
        Assert.NotEqual(Guid.Empty, (Guid)tokenBody["token"]!);
        using var revoked = await admin.DeleteAsync($"/api/v1/admin/contents/{(Guid)first["id"]!}/preview-token");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        using var counts = await admin.GetAsync("/api/v1/admin/contents/counts?kind=Post");
        var countsBody = JsonNode.Parse(await counts.Content.ReadAsStringAsync())!;
        Assert.Equal(1, (int)countsBody["published"]!);
        Assert.Equal(0, (int)countsBody["drafts"]!);
    }

    [PostgresFact]
    public async Task Export_analyze_and_apply_round_trip()
    {
        using var actor = await App.AdministratorAsync();
        await using var api = await StartApiAsync();
        using var admin = await CreateAdminClientAsync(api, await App.AdminSecurityStampAsync());

        using var created = await admin.PostAsJsonAsync("/api/v1/admin/contents",
            new { title = "匯出文章", slug = "匯出文章", kind = "Post", markdown = "匯出內容", status = "Published" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var exported = await admin.GetAsync("/api/v1/admin/transfer/export");
        Assert.True(exported.StatusCode == HttpStatusCode.OK,
            "匯出失敗：" + (int)exported.StatusCode + " | " + string.Join(" || ", api.ConsoleErrors));
        var bytes = await exported.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 100, "匯出檔不應為空。");

        using var analyzeForm = new MultipartFormDataContent();
        using var analyzeFile = new ByteArrayContent(bytes);
        analyzeFile.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        analyzeForm.Add(analyzeFile, "file", "origami-export.json");
        using var analyzed = await admin.PostAsync("/api/v1/admin/transfer/import/analyze", analyzeForm);
        Assert.Equal(HttpStatusCode.OK, analyzed.StatusCode);

        using var applyForm = new MultipartFormDataContent();
        using var applyFile = new ByteArrayContent(bytes);
        applyFile.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        applyForm.Add(applyFile, "file", "origami-export.json");
        using var applied = await admin.PostAsync("/api/v1/admin/transfer/import/apply", applyForm);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);

        using var publicList = await api.CreateClient().GetAsync("/api/v1/posts");
        var publicBody = JsonNode.Parse(await publicList.Content.ReadAsStringAsync())!;
        Assert.Contains(publicBody["items"]!.AsArray(), item => item!["slug"]!.GetValue<string>() == "匯出文章");
    }

    private static async Task<string> VersionAfterTrash(HttpClient admin, Guid id)
    {
        using var edit = await admin.GetAsync($"/api/v1/admin/contents/{id}");
        return (string)JsonNode.Parse(await edit.Content.ReadAsStringAsync())!["version"]!;
    }

    private static byte[] PngBytes()
    {
        using var bitmap = new SKBitmap(64, 48);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
