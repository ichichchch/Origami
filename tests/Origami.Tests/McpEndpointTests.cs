namespace Origami.Tests;

/// <summary>以真實 HTTPS 驅動 /mcp，並完成 Streamable HTTP 傳輸所需的最小 JSON-RPC 握手。</summary>
[Collection(PostgresCollection.Name)]
public sealed class McpEndpointTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private const string InitializeJson =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"origami-tests","version":"1.0"}}}""";

    [PostgresFact]
    public async Task Mcp_endpoint_exposes_only_read_tools_and_only_published_content()
    {
        using var actor = await App.AdministratorAsync();
        await actor.Content.SaveAsync(new ContentEdit { Title = "公開文章", Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = "公開本文" });
        await actor.Content.SaveAsync(new ContentEdit { Title = "未公開草稿", Kind = ContentKind.Post, Status = ContentStatus.Draft, Markdown = "草稿本文" });

        await using var site = new RunningApi(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var session = await InitializeAsync(client);

        var tools = await CallAsync(client, session, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var names = tools["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).Order().ToArray();
        // 唯讀工具就只有這三個；一旦出現寫入工具即屬安全回歸。
        Assert.Equal(["get_page", "get_post", "search_posts"], names);

        var fetched = await CallAsync(client, session,
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_post","arguments":{"slug":"公開文章"}}}""");
        var published = JsonNode.Parse(ToolText(fetched))!;
        Assert.True((bool)published["found"]!, "get_post 未回傳公開內容：" + ToolText(fetched));
        Assert.Equal("公開本文", (string?)published["content"]!["markdown"]);

        var hidden = await CallAsync(client, session,
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_post","arguments":{"slug":"未公開草稿"}}}""");
        var draft = JsonNode.Parse(ToolText(hidden))!;
        Assert.False((bool)draft["found"]!, "草稿不應被找到：" + ToolText(hidden));
    }

    [PostgresFact]
    public async Task Search_tool_fails_gracefully_without_an_embedding_key()
    {
        await using var site = new RunningApi(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var session = await InitializeAsync(client);
        using var response = await SendAsync(client, session.Id, session.Protocol, """
            {"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"search_posts","arguments":{"query":"任何字"}}}
            """);
        // 缺少供應商金鑰屬工具層級結果，絕不能回 5xx 或洩出堆疊內容。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseAsync(response);
        Assert.True(body["result"]?["isError"]?.GetValue<bool>() == true || body["error"] is not null);
    }

    [PostgresFact]
    public async Task Mcp_endpoint_is_rate_limited_per_source()
    {
        await using var site = new RunningApi(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var statuses = new List<int>();
        for (var i = 0; i < 40 && !statuses.Contains(429); i++)
        {
            using var response = await SendAsync(client, null, null, """{"jsonrpc":"2.0","id":1,"method":"ping"}""");
            statuses.Add((int)response.StatusCode);
        }
        Assert.True(statuses.Contains(429), "預期 /mcp 在 30 次後限流，實際狀態碼：" + string.Join(",", statuses));
    }

    private static string ToolText(JsonNode response) =>
        response["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private sealed record McpSession(string? Id, string? Protocol);

    private static async Task<McpSession> InitializeAsync(HttpClient client)
    {
        using var response = await SendAsync(client, null, null, InitializeJson);
        await EnsureSuccessAsync(response);
        var session = response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : null;
        var body = await ParseAsync(response);
        var protocol = (string?)body["result"]?["protocolVersion"];
        if (session is not null)
        {
            using var ack = await SendAsync(client, session, protocol, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await EnsureSuccessAsync(ack);
        }
        return new McpSession(session, protocol);
    }

    private static async Task<JsonNode> CallAsync(HttpClient client, McpSession session, string json)
    {
        using var response = await SendAsync(client, session.Id, session.Protocol, json);
        await EnsureSuccessAsync(response);
        return await ParseAsync(response);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 500) body = body[..500];
        throw new HttpRequestException($"MCP 請求失敗：{(int)response.StatusCode} {response.ReasonPhrase}：{body}");
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string? session, string? protocol, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        if (protocol is not null) request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", protocol);
        // 回應已被緩衝，因此在請求物件釋放後讀取仍安全。
        return await client.SendAsync(request);
    }

    private static async Task<JsonNode> ParseAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var media = response.Content.Headers.ContentType?.MediaType ?? "";
        if (media.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            text = text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("data:", StringComparison.Ordinal))
                .Select(l => l["data:".Length..].Trim()).FirstOrDefault() ?? "";
        return JsonNode.Parse(text) ?? throw new InvalidOperationException("空的 MCP 回應。");
    }
}
