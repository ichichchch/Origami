namespace Origami.Tests;

/// <summary>
/// OpenAPI 契約必須涵蓋 Api 主機實際註冊的所有端點，避免規格文件與實作漂移。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OpenApiContractTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static readonly Regex GroupPattern = new(@"MapGroup\(""(?<prefix>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex RoutePattern = new(
        @"\b(?<receiver>group|app)\.Map(Get|Post|Put|Delete|Patch)\(""(?<template>[^""]*)""",
        RegexOptions.Compiled);

    [PostgresFact]
    public async Task Document_lists_every_registered_route()
    {
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var paths = body["paths"]!.AsObject().Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        var expected = RegisteredRoutes();
        var missing = expected.Except(paths).OrderBy(path => path, StringComparer.Ordinal).ToList();
        var extra = paths.Except(expected).OrderBy(path => path, StringComparer.Ordinal).ToList();

        Assert.Empty(missing);
        Assert.Empty(extra);
    }

    [PostgresFact]
    public async Task Every_documented_operation_carries_a_summary_and_a_tag()
    {
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var operations = body["paths"]!.AsObject()
            .SelectMany(entry => entry.Value!.AsObject().Select(operation => (Path: entry.Key, Method: operation.Key, Operation: operation.Value)))
            .ToList();

        Assert.NotEmpty(operations);
        Assert.All(operations, operation =>
        {
            var summary = operation.Operation!["summary"]!.GetValue<string>();
            var description = operation.Operation!["description"]!.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(summary));
            Assert.False(string.IsNullOrWhiteSpace(description));
            // 禁止退回「查詢 xxx / 建立或執行 xxx」這類中英夾雜的自動拼湊摘要，且名稱保持精簡。
            Assert.DoesNotMatch(@"^(查詢|建立或執行|更新|刪除|呼叫) [a-zA-Z]", summary);
            Assert.True(summary.Length <= 16, $"Operation '{operation.Method} {operation.Path}' summary '{summary}' is too long.");
            Assert.NotEqual(summary, description);
            Assert.NotEmpty(operation.Operation!["tags"]!.AsArray());
            Assert.False(string.IsNullOrWhiteSpace(operation.Operation!["operationId"]!.GetValue<string>()));
        });
    }

    /// <summary>管理端點在契約中必須宣告需要通行證；公開端點則保持匿名。</summary>
    [PostgresFact]
    public async Task Admin_operations_require_bearer_while_public_ones_stay_anonymous()
    {
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var paths = body["paths"]!.AsObject();
        var schemes = body["components"]!["securitySchemes"]!.AsObject();
        var admin = paths.Where(entry => entry.Key.StartsWith("/api/v1/admin", StringComparison.Ordinal)).ToList();
        var anonymous = paths.Where(entry => !entry.Key.StartsWith("/api/v1/admin", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(admin);
        Assert.NotEmpty(anonymous);
        Assert.NotEmpty(schemes);
        // 契約以 ASP.NET Core 預設的 camelCase 序列化，scheme 名稱需與 security 需求一致。
        var scheme = schemes.Select(entry => entry.Key).Single();

        foreach (var operation in admin.SelectMany(entry => entry.Value!.AsObject()))
        {
            var security = operation.Value!["security"];
            Assert.NotNull(security);
            Assert.Contains(security!.AsArray(), requirement => requirement!.AsObject().ContainsKey(scheme));
        }

        foreach (var operation in anonymous.SelectMany(entry => entry.Value!.AsObject()))
        {
            var security = operation.Value!["security"];
            Assert.NotNull(security);
            Assert.Empty(security!.AsArray());
        }
    }

    /// <summary>由 Api 原始碼靜態列出註冊的路由樣式，轉成 OpenAPI 參數寫法。</summary>
    private static HashSet<string> RegisteredRoutes()
    {
        var directory = Path.Combine(RunningSite.FindSolutionRoot(), "src", "Origami.Api");
        var routes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(directory, "*.cs"))
        {
            var text = File.ReadAllText(path);
            var prefixes = GroupPattern.Matches(text).Cast<Match>().Select(match => match.Groups["prefix"].Value).ToList();
            var prefix = prefixes.Count > 0 ? prefixes[0] : "";

            foreach (Match match in RoutePattern.Matches(text))
            {
                var template = match.Groups["template"].Value;
                var full = match.Groups["receiver"].Value == "group" ? prefix + template : template;
                routes.Add(Normalize(full));
            }
        }

        return routes;
    }

    private static string Normalize(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!segment.StartsWith('{') || !segment.EndsWith('}'))
            {
                segments.Add(segment);
                continue;
            }
            var name = segment[1..^1].TrimStart('*');
            var colon = name.IndexOf(':');
            if (colon >= 0) name = name[..colon];
            name = name.TrimEnd('?');
            if (name.Length == 0) continue;
            segments.Add("{" + name + "}");
        }
        return "/" + string.Join('/', segments);
    }
}
