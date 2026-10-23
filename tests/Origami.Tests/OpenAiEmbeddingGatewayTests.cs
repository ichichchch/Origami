namespace Origami.Tests;

public sealed class OpenAiEmbeddingGatewayTests
{
    [Fact]
    public async Task Missing_api_key_is_a_friendly_error()
    {
        var gateway = Gateway(_ => { });
        var error = await Assert.ThrowsAsync<CmsException>(() => gateway.EmbedQueryAsync("文字"));
        Assert.Contains("尚未設定", error.Message);
    }

    [Fact]
    public async Task Results_are_returned_in_input_order_even_when_the_provider_shuffles()
    {
        var first = new float[Limits.EmbeddingDimensions];
        var second = new float[Limits.EmbeddingDimensions];
        first[0] = 1;
        second[1] = 1;
        var calls = 0;
        var gateway = Gateway(o => o.Embedding.ApiKey = "test-key", handler: _ =>
        {
            calls++;
            return Task.FromResult(Response(
                "{\"data\":[{\"index\":1,\"embedding\":" + Json(second) +
                "},{\"index\":0,\"embedding\":" + Json(first) + "}],\"usage\":{\"prompt_tokens\":1}}"));
        });
        var vectors = await gateway.EmbedBatchAsync(["甲", "乙"]);
        Assert.Equal(1, calls);
        Assert.Equal(2, vectors.Count);
        Assert.Equal(1, vectors[0][0]);
        Assert.Equal(1, vectors[1][1]);
    }

    private static string Json(float[] vector) => "[" + string.Join(",", vector) + "]";

    [Fact]
    public async Task Provider_failure_is_a_friendly_error_without_body_details()
    {
        var gateway = Gateway(o => o.Embedding.ApiKey = "test-key", handler: _ =>
            Task.FromResult(Response("""{"error":{"message":"internal secret detail"}}""", HttpStatusCode.InternalServerError)));
        var error = await Assert.ThrowsAsync<CmsException>(() => gateway.EmbedQueryAsync("文字"));
        Assert.Contains("暫時無法使用", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task Dimension_mismatch_is_reported_as_configuration()
    {
        var gateway = Gateway(o => o.Embedding.ApiKey = "test-key", handler: _ =>
            Task.FromResult(Response("""{"data":[{"index":0,"embedding":[0.1,0.2]}],"usage":{"prompt_tokens":1}}""")));
        var error = await Assert.ThrowsAsync<CmsException>(() => gateway.EmbedQueryAsync("文字"));
        Assert.Contains("維度", error.Message);
    }

    [Fact]
    public async Task Api_key_travels_only_in_the_authorization_header()
    {
        HttpRequestMessage? captured = null;
        var gateway = Gateway(o => o.Embedding.ApiKey = "secret-key", handler: request =>
        {
            captured = request;
            return Task.FromResult(Response($$"""{"data":[{"index":0,"embedding":[{{string.Join(',', Enumerable.Repeat("0.1", Limits.EmbeddingDimensions))}}]}]}"""));
        });
        await gateway.EmbedQueryAsync("文字");
        Assert.Equal("Bearer secret-key", captured!.Headers.Authorization?.ToString());
    }

    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static OpenAiEmbeddingGateway Gateway(Action<AiOptions> configure,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? handler = null)
    {
        var options = new AiOptions();
        configure(options);
        IOptionsMonitor<AiOptions> monitor = new OptionsMonitor<AiOptions>(
            new OptionsFactory<AiOptions>([new ConfigureOptions<AiOptions>(configure)], []),
            [], new OptionsCache<AiOptions>());
        var client = new HttpClient(handler != null ? new StubHandler(handler) : new StubHandler(_ => throw new InvalidOperationException("no request expected")));
        return new(client, monitor, NullLogger<OpenAiEmbeddingGateway>.Instance);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Yield();
            return await responder(request);
        }
    }
}
