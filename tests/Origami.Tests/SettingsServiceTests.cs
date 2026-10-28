namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SettingsServiceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Settings_are_seeded_by_migrations_and_public_reads_are_detached()
    {
        using var visitor = App.Anonymous();
        var first = await visitor.Settings.GetAsync();
        Assert.Equal(1, first.Id);
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
        Assert.NotEqual(Guid.Empty, first.Version);
        var original = first.Name;
        first.Name = "An unsaved edit";
        Assert.Equal(original, (await visitor.Settings.GetAsync()).Name);
        await using var db = await App.CreateContextAsync();
        Assert.Equal(1, await db.Settings.CountAsync());
    }

    [PostgresFact]
    public async Task Untrusted_settings_round_trip_as_text_and_the_real_layout_HTML_encodes_them()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.Name = "<script>alert(1)</script> & \"Site\"";
        settings.Description = "<img src=x onerror=alert(1)> '; DROP TABLE \"Contents\"; --";
        await admin.Settings.SaveAsync(settings);
        using var visitor = App.Anonymous();
        var stored = await visitor.Settings.GetAsync();
        Assert.Equal(settings.Name, stored.Name);
        Assert.Equal(settings.Description, stored.Description);
        await using (var db = await App.CreateContextAsync()) Assert.Equal(0, await db.Contents.CountAsync());

        // MainLayout 經 BFF 通道（OrigamiClient）取得站點設定與導覽；隔離渲染時以樁接面
        // 回傳「剛儲存的設定」，維持本測試的原始意圖：真實版面必須輸出編碼後的不可信內容。
        var projection = new PublicSiteSettings(settings.Name, settings.Description, null, null, null, "", null, null);
        // 隔離渲染沒有工作階段：AdminSession 以匿名狀態回應，戳記 header 不會附加。
        var session = new AdminSession(new HttpContextAccessor(), new AnonymousAuthenticationStateProvider(),
            Options.Create(new IdentityOptions()));
        var api = new OrigamiClient(new HttpClient(new StubApiHandler(projection)) { BaseAddress = new Uri("http://stub/") }, session);
        await using var renderer = new HtmlRenderer(new StubApiProvider(visitor.Services, api),
            visitor.Services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<MainLayout>(ParameterView.Empty)).ToHtmlString());
        Assert.Contains(HtmlEncoder.Default.Encode(settings.Name), html);
        Assert.Contains(HtmlEncoder.Default.Encode(settings.Description), html);
        Assert.DoesNotContain(settings.Name, html);
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Stale_settings_writes_cannot_overwrite_the_winning_actor()
    {
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync();
        var left = await first.Settings.GetAsync();
        var right = await second.Settings.GetAsync();
        left.Name = "Winning site name";
        left.Description = "Winning description";
        var saved = await first.Settings.SaveAsync(left);
        Assert.NotEqual(right.Version, saved.Version);
        right.Name = "Stale overwrite";
        await Assert.ThrowsAsync<CmsException>(() => second.Settings.SaveAsync(right));
        var after = await second.Settings.GetAsync();
        Assert.Equal(saved.Name, after.Name);
        Assert.Equal(saved.Description, after.Description);
        Assert.Equal(saved.Version, after.Version);
    }

    [PostgresTheory]
    [InlineData("blank-name")]
    [InlineData("long-name")]
    [InlineData("long-description")]
    [InlineData("wrong-id")]
    public async Task Invalid_settings_do_not_modify_the_singleton(string scenario)
    {
        using var admin = await App.AdministratorAsync();
        var original = await admin.Settings.GetAsync();
        var input = await admin.Settings.GetAsync();
        switch (scenario)
        {
            case "blank-name": input.Name = " \t"; break;
            case "long-name": input.Name = new string('x', Limits.SiteName + 1); break;
            case "long-description": input.Description = new string('x', Limits.Description + 1); break;
            case "wrong-id": input.Id = 2; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(input));
        var after = await admin.Settings.GetAsync();
        Assert.Equal(original.Name, after.Name);
        Assert.Equal(original.Description, after.Description);
        Assert.Equal(original.Version, after.Version);
    }

    [PostgresFact]
    public async Task Exact_setting_bounds_and_empty_description_are_accepted_and_trimmed()
    {
        using var admin = await App.AdministratorAsync();
        var input = await admin.Settings.GetAsync();
        input.Name = new string('n', Limits.SiteName);
        input.Description = new string('d', Limits.Description);
        await admin.Settings.SaveAsync(input);
        var saved = await admin.Settings.GetAsync();
        Assert.Equal(input.Name, saved.Name);
        Assert.Equal(input.Description, saved.Description);
        saved.Name = "  Trimmed name  ";
        saved.Description = "  Trimmed description  ";
        await admin.Settings.SaveAsync(saved);
        saved = await admin.Settings.GetAsync();
        Assert.Equal("Trimmed name", saved.Name);
        Assert.Equal("Trimmed description", saved.Description);
        saved.Description = null!;
        await admin.Settings.SaveAsync(saved);
        Assert.Equal("", (await admin.Settings.GetAsync()).Description);
    }

    /// <summary>僅替換 OrigamiClient 一項服務，其餘全部沿用測試應用本身的供應器。</summary>
    private sealed class StubApiProvider(IServiceProvider inner, OrigamiClient api) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(OrigamiClient) ? api : inner.GetService(serviceType);
    }

    /// <summary>一律回報匿名訪客；隔離渲染場景沒有可用的工作階段。</summary>
    private sealed class AnonymousAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    /// <summary>模擬 Api 的兩個公開讀取端點：回傳固定站點設定與空導覽清單。</summary>
    private sealed class StubApiHandler(PublicSiteSettings settings) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri?.AbsolutePath.EndsWith("/api/v1/navigation") == true
                ? "[]"
                : JsonSerializer.Serialize(settings, Json);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
