using System.Net;
using System.Text.Json;
using Origami.Mobile.Models;
using Origami.Mobile.Services;

namespace Origami.Mobile.Tests;

/// <summary>
/// Parses payloads exactly as the Origami Api serializes them (camelCase, enums as strings,
/// extra fields present in server records must be ignored).
/// </summary>
public sealed class ApiContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Paged_post_list_parses_camelCase_and_string_enums()
    {
        const string json = """
        {
          "items": [
            {
              "id": "0f1c4a6e-5b3d-4c2a-9e8f-7a6b5c4d3e2f",
              "kind": "Post", "title": "列表文章", "slug": "列表文章", "status": "Published",
              "updatedAt": "2026-10-26T08:00:00Z", "publishedAt": "2026-10-26T07:30:00Z",
              "excerpt": "摘要", "excerptIsManual": true, "isSticky": true, "isProtected": false,
              "version": "1a2b3c4d-0000-0000-0000-000000000000"
            }
          ],
          "total": 3, "page": 1, "pageSize": 10
        }
        """;
        var result = JsonSerializer.Deserialize<ApiPagedResult<ApiContentSummary>>(json, JsonOptions)!;
        Assert.Equal(3, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("列表文章", item.Title);
        Assert.Equal("Post", item.Kind);
        Assert.True(item.IsSticky);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 7, 30, 0, TimeSpan.Zero), item.PublishedAt);
    }

    [Fact]
    public void Detail_parses_markdown_and_taxonomies()
    {
        const string json = """
        {
          "id": "0f1c4a6e-5b3d-4c2a-9e8f-7a6b5c4d3e2f",
          "kind": "Page", "title": "公開頁面", "slug": "公開頁面",
          "markdown": "# 標題\n內容",
          "publishedAt": "2026-10-26T07:30:00Z", "excerpt": "",
          "categories": [{ "name": "公告", "slug": "公告" }],
          "tags": []
        }
        """;
        var detail = JsonSerializer.Deserialize<ApiContentDetail>(json, JsonOptions)!;
        Assert.Equal("Page", detail.Kind);
        Assert.Contains("# 標題", detail.Markdown);
        Assert.Equal("公告", Assert.Single(detail.Categories).Name);
    }

    [Fact]
    public void Search_result_parses_hits_and_semantic_flag()
    {
        const string json = """
        { "hits": [ { "id": "0f1c4a6e-5b3d-4c2a-9e8f-7a6b5c4d3e2f", "title": "搜尋文章", "slug": "搜尋文章", "snippet": "…片段…", "source": "關鍵字" } ], "semanticAvailable": false }
        """;
        var result = JsonSerializer.Deserialize<ApiSearchResult>(json, JsonOptions)!;
        Assert.False(result.SemanticAvailable);
        Assert.Equal("關鍵字", Assert.Single(result.Hits).Source);
    }

    [Fact]
    public async Task Client_maps_not_found_to_null_and_parses_success()
    {
        var handler = new FakeHandler();
        handler.Queue(HttpStatusCode.NotFound, "{}");
        handler.Queue(HttpStatusCode.OK, """
            { "id": "0f1c4a6e-5b3d-4c2a-9e8f-7a6b5c4d3e2f", "kind": "Post", "title": "文章", "slug": "文章",
              "markdown": "本文", "publishedAt": null, "excerpt": "",
              "categories": [], "tags": [] }
            """);
        var client = new OrigamiApiClient(new AppSettings { BaseUrl = "http://localhost:51999" }, handler);

        Assert.Null(await client.GetContentAsync("post", "草稿"));
        var found = await client.GetContentAsync("post", "文章");
        Assert.NotNull(found);
        Assert.Equal("文章", found!.Title);
        var path = handler.Requests[0].RequestUri!.AbsolutePath;
        Assert.StartsWith("/api/v1/posts/", path);
    }

    [Fact]
    public async Task Client_surfaces_rate_limit_as_a_friendly_error()
    {
        var handler = new FakeHandler();
        handler.Queue(HttpStatusCode.TooManyRequests, "Too many requests.");
        var client = new OrigamiApiClient(new AppSettings { BaseUrl = "http://localhost:51999" }, handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync("字"));
        Assert.Contains("限流", error.Message);
    }

    /// <summary>A stub transport with scripted responses; no real network is involved.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> responses = [];
        public List<HttpRequestMessage> Requests { get; } = [];

        public void Queue(HttpStatusCode status, string body) => responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var (status, body) = responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
