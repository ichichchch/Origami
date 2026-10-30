namespace Origami.Tests;

/// <summary>Scalar API 參考文件必須提供可用頁面、就地提供前端資源，並指向本機自建的 OpenAPI 契約。</summary>
[Collection(PostgresCollection.Name)]
public sealed class ScalarApiReferenceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Reference_page_renders_and_points_at_the_local_contract()
    {
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();

        using var response = await client.GetAsync("/scalar");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        // 契約以相對路徑輸出，由官方 helper 依 base path 解析；根路徑部署即 /openapi/v1.json。
        Assert.Contains("openapi/v1.json", html);
    }

    [PostgresFact]
    public async Task Reference_assets_are_served_by_the_host()
    {
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();

        // 參考頁的腳本與樣式由套件內嵌提供，不依賴外部 CDN。
        using var script = await client.GetAsync("/scalar/scalar.js");
        script.EnsureSuccessStatusCode();
        Assert.NotEqual(0, script.Content.Headers.ContentLength ?? 0);
    }

    [PostgresFact]
    public async Task Contract_referenced_by_the_page_is_publicly_readable()
    {
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotEmpty(body["paths"]!.AsObject());
    }
}
