namespace Origami.Tests;

/// <summary>媒體批次上傳：逐檔驗證與回報、單檔失敗不影響其他檔案、空批與超量整批拒絕。</summary>
[Collection(PostgresCollection.Name)]
public sealed class MediaBatchUploadTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string Token = "isolated-test-service-token-0123456789abcdef";

    // Api 以字串序列化列舉（與 Web 預設不同），比對時須採相同設定。
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        bitmap.Erase(SKColors.SeaGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90)!;
        return encoded.ToArray();
    }

    private static ByteArrayContent PngContent(byte[] bytes, string contentType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    [PostgresFact]
    public async Task Batch_upload_reports_per_file_results_and_tolerates_individual_failures()
    {
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var admin = api.CreateBffClient(Token, await App.AdminUserIdAsync(), await App.AdminSecurityStampAsync());

        using var form = new MultipartFormDataContent();
        form.Add(PngContent(Png(8, 8)), "files", "good.png");
        form.Add(PngContent([1, 2, 3]), "files", "broken.png");
        using var response = await admin.PostAsync("/api/v1/admin/media/batch", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<MediaBatchItem>>(Json);
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
        // 表單順序即回報順序：合法圖片成功，無法解碼者失敗且附原因。
        Assert.True(results[0].Succeeded);
        Assert.NotNull(results[0].Item);
        Assert.Equal("good.png", results[0].FileName);
        Assert.False(results[1].Succeeded);
        Assert.Null(results[1].Item);
        Assert.False(string.IsNullOrEmpty(results[1].Message));

        // 成功者確實落庫。
        using var list = await admin.GetAsync("/api/v1/admin/media?pageSize=50");
        list.EnsureSuccessStatusCode();
        var media = await list.Content.ReadFromJsonAsync<PagedResult<MediaItem>>(Json);
        Assert.Contains(media!.Items, x => x.OriginalName == "good.png");
    }

    [PostgresFact]
    public async Task Batch_upload_rejects_empty_and_over_limit_batches()
    {
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: Token);
        using var admin = api.CreateBffClient(Token, await App.AdminUserIdAsync(), await App.AdminSecurityStampAsync());

        using (var empty = new MultipartFormDataContent())
        {
            using var response = await admin.PostAsync("/api/v1/admin/media/batch", empty);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var over = new MultipartFormDataContent();
        for (var i = 0; i <= Limits.MediaBatchMax; i++)
            over.Add(PngContent([1]), "files", $"file{i}.png");
        using var rejected = await admin.PostAsync("/api/v1/admin/media/batch", over);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var message = await rejected.Content.ReadFromJsonAsync<Dictionary<string, string>>(Json);
        Assert.Contains($"{Limits.MediaBatchMax}", message!["message"], StringComparison.Ordinal);
    }
}
