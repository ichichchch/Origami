using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Origami.Web.Services;

public sealed record PublicSiteSettings(string Name, string Description, int? PostsPerPage, int? FeedItems,
    bool? FeedShowFullText, string AuthorDisplayName, Guid? HomePageContentId, Guid? SiteIconMediaId)
{
    /// <summary>Api 無法回應時的後備站名與說明；全站單一來源。</summary>
    public static PublicSiteSettings Default { get; } = new("Origami", "用文字，分享你的世界。", null, null, null, "", null, null);
}

public sealed record ApiPreview(PublicContent Content, string? PublishedKind, string? PublishedSlug);

public sealed record ApiFeedItem(Guid Id, string Title, string Slug, DateTimeOffset? PublishedAt,
    string Excerpt, string Markdown, bool ExcerptIsManual);

/// <summary>
/// BFF 資料通道：網站的頁面資料讀寫一律經此用戶端呼叫 Origami.Api；瀏覽器不持有任何 API 憑證。
/// 三個 API 投影 record 與 Api 端形狀保持同步。
/// </summary>
public sealed class OrigamiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // ---------- 公開讀取 ----------

    public Task<PublicSiteSettings?> GetPublicSettingsAsync(CancellationToken ct = default) =>
        GetAsync<PublicSiteSettings>("api/v1/settings", ct);

    public Task<List<PageNavItem>?> GetNavigationAsync(CancellationToken ct = default) =>
        GetAsync<List<PageNavItem>>("api/v1/navigation", ct);

    public Task<List<ContentNeighbor>?> GetNeighborsAsync(Guid contentId, CancellationToken ct = default) =>
        GetAsync<List<ContentNeighbor>>($"api/v1/contents/{contentId}/neighbors", ct);

    public Task<List<SemanticHit>?> GetRelatedAsync(Guid contentId, CancellationToken ct = default) =>
        GetAsync<List<SemanticHit>>($"api/v1/contents/{contentId}/related", ct);

    public Task<List<ArchiveMonth>?> GetArchivesAsync(CancellationToken ct = default) =>
        GetAsync<List<ArchiveMonth>>("api/v1/archives", ct);

    public Task<List<PublicTaxonomy>?> GetPopularCategoriesAsync(CancellationToken ct = default) =>
        GetAsync<List<PublicTaxonomy>>("api/v1/categories/popular", ct);

    public Task<TaxonomyView?> GetTaxonomyViewAsync(TaxonomyKind kind, string slug, CancellationToken ct = default) =>
        GetAsync<TaxonomyView>($"api/v1/taxonomies/{kind.ToString().ToLowerInvariant()}/{Uri.EscapeDataString(slug)}", ct);

    public Task<SlugRedirectTarget?> ResolveRedirectAsync(string slug, CancellationToken ct = default) =>
        GetAsync<SlugRedirectTarget>($"api/v1/redirects/{Uri.EscapeDataString(slug)}", ct);

    public Task<ApiPreview?> GetPreviewAsync(Guid token, CancellationToken ct = default) =>
        GetAsync<ApiPreview>($"api/v1/preview/{token}", ct);

    public Task<List<SitemapItem>?> GetSitemapAsync(CancellationToken ct = default) =>
        GetAsync<List<SitemapItem>>("api/v1/sitemap", ct);

    public async Task<List<ApiFeedItem>?> GetFeedItemsAsync(string? category = null, string? tag = null, CancellationToken ct = default)
    {
        var query = "api/v1/feed";
        if (category is not null) query += "?category=" + Uri.EscapeDataString(category);
        if (tag is not null) query += (category is null ? "?" : "&") + "tag=" + Uri.EscapeDataString(tag);
        return await GetAsync<List<ApiFeedItem>>(query, ct);
    }

    public Task<PublicSearchService.Result?> SearchAsync(string query, int limit = Limits.PageSize, int page = 1, CancellationToken ct = default) =>
        GetAsync<PublicSearchService.Result>($"api/v1/search?q={Uri.EscapeDataString(query)}&limit={limit}&page={page}", ct);

    public async Task<PagedResult<ContentSummary>?> ListPublishedAsync(int page = 1, int pageSize = Limits.PageSize,
        string? category = null, string? tag = null, int? year = null, int? month = null, CancellationToken ct = default)
    {
        var query = $"api/v1/posts?page={page}&pageSize={pageSize}";
        if (category is not null) query += "&category=" + Uri.EscapeDataString(category);
        if (tag is not null) query += "&tag=" + Uri.EscapeDataString(tag);
        if (year is { } y) query += "&year=" + y;
        if (month is { } m) query += "&month=" + m;
        return await GetAsync<PagedResult<ContentSummary>>(query, ct);
    }

    public Task<PublicContent?> GetPublishedPageByIdAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<PublicContent>($"api/v1/contents/{id}/published", ct);

    public Task<Dictionary<string, int>?> GetPublicImageWidthsAsync(CancellationToken ct = default) =>
        GetAsync<Dictionary<string, int>>("api/v1/media/widths", ct);

    // ---------- 管理面（BFF 通行證）----------

    public async Task<PagedResult<ContentSummary>?> ListAdminAsync(ContentKind kind, int page = 1,
        int pageSize = Limits.PageSize, ContentStatus? status = null, string? search = null, CancellationToken ct = default)
    {
        var query = $"api/v1/admin/contents?kind={kind}&page={page}&pageSize={pageSize}";
        if (status is { } value) query += "&status=" + value;
        if (!string.IsNullOrWhiteSpace(search)) query += "&search=" + Uri.EscapeDataString(search.Trim());
        return await GetAsync<PagedResult<ContentSummary>>(query, ct);
    }

    public Task<ContentCounts?> CountAdminAsync(ContentKind kind, CancellationToken ct = default) =>
        GetAsync<ContentCounts>($"api/v1/admin/contents/counts?kind={kind}", ct);

    public Task<ContentEdit?> GetEditAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<ContentEdit>($"api/v1/admin/contents/{id}", ct);

    /// <summary>網站渲染用的完整詳情：包含受保護與私密內容的旗標與本文（僅 BFF 通行證可達）。</summary>
    public Task<PublicContent?> GetPublishedDetailAsync(ContentKind kind, string slug, CancellationToken ct = default) =>
        GetAsync<PublicContent>($"api/v1/admin/contents/published/{kind.ToString().ToLowerInvariant()}/{Uri.EscapeDataString(slug)}", ct);

    /// <summary>以路徑段解析已發布頁面（含父鏈驗證）；回傳內容與完整階層路徑段。</summary>
    public Task<PagePathContent?> ResolvePublishedPagePathAsync(IReadOnlyList<string> segments, CancellationToken ct = default) =>
        GetAsync<PagePathContent>($"api/v1/admin/contents/published-page/{string.Join("/", segments.Select(Uri.EscapeDataString))}", ct);

    public async Task<bool> GetUnlockStateAsync(Guid contentId, string? unlockToken, CancellationToken ct = default)
    {
        // 解鎖 token 走自訂標頭而非查詢字串，避免進入任何以 URL 為本的日誌。
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/admin/contents/{contentId}/unlock-state");
        if (!string.IsNullOrEmpty(unlockToken)) request.Headers.Add("X-Unlock-Token", unlockToken);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccessAsync(response);
        var state = await response.Content.ReadFromJsonAsync<UnlockState>(Json, ct);
        return state?.Unlocked == true;
    }

    public async Task<string?> UnlockAsync(Guid contentId, string password, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/admin/contents/{contentId}/unlock")
        {
            Content = JsonContent.Create(new { password }, options: Json)
        };
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        return body.GetProperty("token").GetString();
    }

    public async Task RecordViewAsync(Guid contentId, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/v1/admin/contents/{contentId}/view", null, ct);
        await EnsureSuccessAsync(response);
    }

    public Task<List<PageOption>?> PageOptionsAsync(CancellationToken ct = default) =>
        GetAsync<List<PageOption>>("api/v1/admin/pages/options", ct);

    public Task<List<RevisionItem>?> ListRevisionsAsync(Guid contentId, CancellationToken ct = default) =>
        GetAsync<List<RevisionItem>>($"api/v1/admin/contents/{contentId}/revisions", ct);

    public Task<RevisionDetail?> GetRevisionAsync(Guid revisionId, CancellationToken ct = default) =>
        GetAsync<RevisionDetail>($"api/v1/admin/revisions/{revisionId}", ct);

    public async Task<Guid> CyclePreviewTokenAsync(Guid contentId, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/v1/admin/contents/{contentId}/preview-token", null, ct);
        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        return body.GetProperty("token").GetGuid();
    }

    public async Task RevokePreviewTokenAsync(Guid contentId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/v1/admin/contents/{contentId}/preview-token", ct);
        await EnsureSuccessAsync(response);
    }

    public Task<ContentEdit?> CreateContentAsync(ContentEdit input, CancellationToken ct = default) =>
        SendContentAsync<ContentEdit>(HttpMethod.Post, "api/v1/admin/contents", input, ct);

    public Task<ContentEdit?> UpdateContentAsync(Guid id, ContentEdit input, CancellationToken ct = default) =>
        SendContentAsync<ContentEdit>(HttpMethod.Put, $"api/v1/admin/contents/{id}", input, ct);

    public async Task TrashContentAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/v1/admin/contents/{id}?version={version}", ct);
        await EnsureSuccessAsync(response);
    }

    public async Task PurgeContentAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/v1/admin/contents/{id}?version={version}&permanent=true", ct);
        await EnsureSuccessAsync(response);
    }

    public async Task RestoreContentAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/v1/admin/contents/{id}/restore?version={version}", null, ct);
        await EnsureSuccessAsync(response);
    }

    /// <summary>清單批次操作；回傳逐筆結果，單筆失敗不影響其他項目。</summary>
    public async Task<List<BulkItemResult>?> BulkContentsAsync(BulkAction action,
        IReadOnlyList<(Guid Id, Guid Version)> items, CancellationToken ct = default)
    {
        return await SendAsync<List<BulkItemResult>>(
            new(HttpMethod.Post, "api/v1/admin/contents/bulk"),
            new { action = action.ToString().ToLowerInvariant(), items = items.Select(x => new { id = x.Id, version = x.Version }) }, ct);
    }

    public async Task<PagedResult<TaxonomyItem>?> ListTaxonomiesAsync(TaxonomyKind kind, int page = 1,
        int pageSize = Limits.MaxPageSize, CancellationToken ct = default) =>
        await GetAsync<PagedResult<TaxonomyItem>>($"api/v1/admin/taxonomies?kind={kind}&page={page}&pageSize={pageSize}", ct);

    public Task<TaxonomyEdit?> SaveTaxonomyAsync(TaxonomyKind kind, TaxonomyEdit input, CancellationToken ct = default) =>
        input.Id is { } id
            ? SendAsync<TaxonomyEdit>(new(HttpMethod.Put, $"api/v1/admin/taxonomies/{id}?kind={kind}"), input, ct)
            : SendAsync<TaxonomyEdit>(new(HttpMethod.Post, $"api/v1/admin/taxonomies?kind={kind}"), input, ct);

    public async Task DeleteTaxonomyAsync(TaxonomyKind kind, Guid id, Guid version, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/v1/admin/taxonomies/{id}?kind={kind}&version={version}", ct);
        await EnsureSuccessAsync(response);
    }

    public async Task<PagedResult<MediaItem>?> ListMediaAsync(int page = 1, int pageSize = Limits.PageSize,
        string? search = null, CancellationToken ct = default) =>
        await GetAsync<PagedResult<MediaItem>>(
            $"api/v1/admin/media?page={page}&pageSize={pageSize}" +
            (string.IsNullOrWhiteSpace(search) ? "" : "&search=" + Uri.EscapeDataString(search.Trim())), ct);

    public async Task<MediaItem?> UploadMediaAsync(byte[] bytes, string fileName, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        using var response = await http.PostAsync("api/v1/admin/media", form, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<MediaItem>(Json, ct);
    }

    public Task<MediaItem?> UpdateMediaMetaAsync(Guid id, string? altText, string? caption, CancellationToken ct = default) =>
        SendAsync<MediaItem>(new(HttpMethod.Put, $"api/v1/admin/media/{id}/meta"), new { altText, caption }, ct);

    public Task<List<MediaService.MediaReference>?> ListMediaReferencesAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<List<MediaService.MediaReference>>($"api/v1/admin/media/{id}/references", ct);

    public async Task DeleteMediaAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/v1/admin/media/{id}?confirmed=true", ct);
        await EnsureSuccessAsync(response);
    }

    public Task<SiteSettings?> GetSettingsAsync(CancellationToken ct = default) =>
        GetAsync<SiteSettings>("api/v1/admin/settings", ct);

    public Task<SiteSettings?> SaveSettingsAsync(SiteSettings input, CancellationToken ct = default) =>
        SendAsync<SiteSettings>(new(HttpMethod.Put, "api/v1/admin/settings"), input, ct);

    public async Task<byte[]> ExportAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/v1/admin/transfer/export", ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public Task<ImportService.ImportPreview?> AnalyzeImportAsync(byte[] bytes, CancellationToken ct = default) =>
        SendFileAsync<ImportService.ImportPreview>("api/v1/admin/transfer/import/analyze", bytes, ct);

    public Task<ImportService.ImportPreview?> ApplyImportAsync(byte[] bytes, CancellationToken ct = default) =>
        SendFileAsync<ImportService.ImportPreview>("api/v1/admin/transfer/import/apply", bytes, ct);

    private sealed record UnlockState(bool Unlocked);

    // ---------- 共用傳輸 ----------

    private async Task<T?> SendContentAsync<T>(HttpMethod method, string url, ContentEdit input, CancellationToken ct)
    {
        input.Id = method == HttpMethod.Post ? null : ExtractId(url);
        return await SendAsync<T>(new(method, url), input, ct);
    }

    private static Guid? ExtractId(string url)
    {
        var tail = url.Replace("api/v1/admin/contents/", "", StringComparison.Ordinal);
        return Guid.TryParse(tail, out var id) ? id : null;
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, object body, CancellationToken ct)
    {
        request.Content = JsonContent.Create(body, options: Json);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private async Task<T?> SendFileAsync<T>(string url, byte[] bytes, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", "origami-export.json");
        using var response = await http.PostAsync(url, form, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("API 拒絕存取，請確認網站主機的服務通行證設定。");
        // API 端的驗證訊息以 CmsException 原樣呈現，頁面錯誤處理因此不必改寫。
        string message;
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            message = JsonNode.Parse(text)?["message"]?.ToString() ?? "";
        }
        catch (JsonException)
        {
            message = "";
        }
        throw new CmsException(message.Length > 0 ? message : $"API 要求失敗（{(int)response.StatusCode}）。");
    }
}
