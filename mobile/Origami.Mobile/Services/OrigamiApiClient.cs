using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Origami.Mobile.Models;

namespace Origami.Mobile.Services;

/// <summary>Typed client for the read-only Origami /api/v1 endpoints. Drafts and hidden content return null.</summary>
public sealed class OrigamiApiClient(AppSettings settings, HttpMessageHandler? handler = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = handler is null ? new() : new(handler);

    public string BaseUrl => settings.BaseUrl.TrimEnd('/');

    public async Task<ApiPagedResult<ApiContentSummary>> ListPostsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/api/v1/posts?page={page}&pageSize={pageSize}";
        using var response = await http.GetAsync(url, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<ApiPagedResult<ApiContentSummary>>(JsonOptions, ct)
               ?? throw new InvalidOperationException("API 回應不是有效的 JSON。");
    }

    /// <summary>kind is "post" or "page"; null means the slug is unknown, unpublished, protected or private.</summary>
    public async Task<ApiContentDetail?> GetContentAsync(string kind, string slug, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/api/v1/{kind}s/{Uri.EscapeDataString(slug)}";
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<ApiContentDetail>(JsonOptions, ct);
    }

    public async Task<ApiSearchResult> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/api/v1/search?q={Uri.EscapeDataString(query)}&limit={limit}";
        using var response = await http.GetAsync(url, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<ApiSearchResult>(JsonOptions, ct)
               ?? throw new InvalidOperationException("API 回應不是有效的 JSON。");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("請求過於頻繁（API 限流），請稍後再試。", null, response.StatusCode);
        throw new HttpRequestException($"API 要求失敗（{(int)response.StatusCode}）。", null, response.StatusCode);
    }
}
