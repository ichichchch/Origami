namespace Origami.Tests;

/// <summary>僅測試使用的 HTTPS 主機；正式 Identity/OAuth 管線搭配隔離 PostgreSQL 與封閉 backchannel。</summary>
internal sealed class ExternalAuthenticationTestSite : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly X509Certificate2 certificate;
    private readonly HttpClient backchannel;
    public Uri Address { get; }
    public TestClock Clock { get; } = new();
    public OAuthBackchannel Backchannel { get; } = new();
    public IServiceProvider Services => app.Services;

    public ExternalAuthenticationTestSite(string connectionString, HttpClient? apiClient = null,
        SaveChangesInterceptor? saveInterceptor = null, ILoggerProvider? loggerProvider = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Address = new Uri($"https://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        listener.Stop();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));
        // 相對 API URL 也須能進入封閉 handler，不能先因缺少 BaseAddress 而失敗。
        backchannel = new HttpClient(Backchannel) { BaseAddress = new Uri("https://bff.invalid/") };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        if (loggerProvider is not null)
            builder.Logging.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Warning)
                .AddFilter("Origami.Web.Services", LogLevel.Trace)
                .AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Trace);
        var settings = new Dictionary<string, string?>();
        foreach (var provider in new[] { "Google", "Microsoft", "GitHub", "WeChat" })
        {
            var prefix = "Authentication:External:" + provider + ":";
            settings[prefix + "Enabled"] = "true";
            settings[prefix + (provider == "WeChat" ? "AppId" : "ClientId")] = "test-client";
            settings[prefix + (provider == "WeChat" ? "AppSecret" : "ClientSecret")] = "test-only-placeholder-secret";
            settings[prefix + "AllowedOrigins:0"] = Address.GetLeftPart(UriPartial.Authority);
        }
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, Address.Port, listen => listen.UseHttps(certificate)));
        builder.Services.AddScoped<SiteContext>();
        builder.Services.AddDbContextFactory<CmsDbContext>(options =>
        {
            options.UseNpgsql(connectionString, pg => pg.UseVector());
            if (saveInterceptor is not null) options.AddInterceptors(saveInterceptor);
        }, ServiceLifetime.Scoped);
        builder.Services.AddCmsIdentity();
        builder.Services.AddExternalAuthentication(builder.Configuration);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.AddSingleton<LoginThrottle>();
        builder.Services.AddScoped<WebSignInFlow>();
        builder.Services.AddScoped<AdminSession>();
        // 注入的 API transport 由測試持有及釋放；OAuth 永遠只使用封閉 backchannel。
        builder.Services.AddScoped(provider => new OrigamiClient(apiClient ?? backchannel,
            provider.GetRequiredService<AdminSession>(), provider.GetRequiredService<SiteContext>()));
        builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme,
            options => options.TimeProvider = Clock);
        builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(IdentityConstants.ExternalScheme,
            options => options.TimeProvider = Clock);
        builder.Services.Configure<SecurityStampValidatorOptions>(options => options.TimeProvider = Clock);
        ConfigureOAuth<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>(builder.Services, "Google");
        ConfigureOAuth<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions>(builder.Services, "Microsoft");
        ConfigureOAuth<Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions>(builder.Services, "GitHub");
        ConfigureOAuth<WeChatOAuthOptions>(builder.Services, "WeChat");
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapCmsIdentity();
        app.MapGet("/test/csrf", (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
            Results.Text(antiforgery.GetAndStoreTokens(context).RequestToken!));
        // 測試專用 transport 仍逐次走正式密碼檢查和 WebSignInFlow，不製造登入 principal。
        app.MapPost("/test/password", async (HttpContext context, WebSignInFlow flow) =>
        {
            var form = await context.Request.ReadFormAsync();
            var user = await flow.CheckPasswordAsync(form["username"].ToString(), form["password"].ToString());
            if (user is null) return Results.Unauthorized();
            var signedIn = await flow.BeginAsync(context, user, form["remember"] == "true");
            return Results.Ok(new { signedIn });
        });
        app.MapPost("/test/code", async (HttpContext context, WebSignInFlow flow) =>
        {
            var form = await context.Request.ReadFormAsync();
            try
            {
                return await flow.CompleteSecondFactorAsync(context, form["code"].ToString()) ? Results.Ok() : Results.Unauthorized();
            }
            catch (Exception error) when (error is CmsException or DbUpdateException or DbException)
            {
                // 與正式 Login.CodeAsync 的失敗邊界一致，transport 只回固定 401。
                return Results.Unauthorized();
            }
        });
        // 使用一般 HTTP handler，不附加 .NET 10 JSON API metadata，保留網站 Cookie challenge 行為。
        app.MapGet("/test/me", async (HttpContext context) =>
        {
            await context.Response.WriteAsJsonAsync(new
            {
                id = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                deadline = context.User.FindFirstValue(AccessClaims.SessionExpiresAt)
            });
        }).RequireAuthorization();
    }

    private void ConfigureOAuth<T>(IServiceCollection services, string name)
        where T : Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions => services.PostConfigure<T>(name, options =>
        {
            options.TimeProvider = Clock;
            options.Backchannel = backchannel;
        });

    public Task StartAsync() => app.StartAsync();

    public HttpClient CreateClient(CookieContainer? cookies = null) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        CookieContainer = cookies ?? new CookieContainer(),
        ServerCertificateCustomValidationCallback = (_, received, _, _) => received?.Thumbprint == certificate.Thumbprint
    }) { BaseAddress = Address };

    public Microsoft.AspNetCore.Authentication.AuthenticationTicket ReadTicket(HttpResponseMessage response,
        string? scheme = null)
    {
        var options = Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(scheme ?? IdentityConstants.ApplicationScheme);
        var header = Assert.Single(response.Headers.GetValues("Set-Cookie")
            .Select(value => Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(value)),
            cookie => cookie.Name == options.Cookie.Name && cookie.Value.Length > 0);
        var ticket = options.TicketDataFormat.Unprotect(header.Value.ToString());
        Assert.NotNull(ticket);
        return ticket;
    }

    public async Task<Uri> ChallengeAsync(HttpClient client, string provider, bool remember)
    {
        var csrf = await client.GetStringAsync("/test/csrf");
        using var response = await client.PostAsync("/account/external/challenge", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = csrf, ["provider"] = provider, ["Input.Remember"] = remember ? "true" : "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.True(response.Headers.Location.IsAbsoluteUri);
        return response.Headers.Location;
    }

    public static string Callback(string provider, Uri challenge, string code = "test-authorization-code")
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(challenge.Query);
        return $"/signin-{provider.ToLowerInvariant()}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(query["state"].ToString())}";
    }

    public Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions OAuthOptions(string provider) => provider switch
    {
        "Google" => Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>>().Get(provider),
        "Microsoft" => Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions>>().Get(provider),
        "GitHub" => Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions>>().Get(provider),
        "WeChat" => Services.GetRequiredService<IOptionsMonitor<WeChatOAuthOptions>>().Get(provider),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    public static string ProviderKey(string provider) => provider switch
    {
        "Google" => "google-test-user",
        "Microsoft" => "microsoft-test-user",
        "GitHub" => "42",
        "WeChat" => "test-client:wechat-test-user",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        backchannel.Dispose();
        certificate.Dispose();
    }

    public sealed class TestClock : TimeProvider
    {
        private long utcTicks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref utcTicks), TimeSpan.Zero);
        public void Set(DateTimeOffset now) => Interlocked.Exchange(ref utcTicks, now.UtcTicks);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref utcTicks, duration.Ticks);
    }

    public sealed class OAuthBackchannel : HttpMessageHandler
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public string? Failure { get; set; }
        public bool WeChatTextPlain { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref calls);
            var target = request.RequestUri!.GetLeftPart(UriPartial.Path);
            if (Failure == "unavailable") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            if (Failure == "invalid-json") return Task.FromResult(Response("not-json", "text/plain"));
            if (Failure == "wechat-error") return Task.FromResult(Response("{\"errcode\":40029,\"errmsg\":\"test-provider-private-error\"}"));
            var payload = target switch
            {
                "https://oauth2.googleapis.com/token" or "https://accounts.google.com/o/oauth2/token" or
                "https://login.microsoftonline.com/common/oauth2/v2.0/token" or "https://github.com/login/oauth/access_token" =>
                    "{\"access_token\":\"test-access-token\",\"refresh_token\":\"test-refresh-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}",
                "https://www.googleapis.com/oauth2/v2/userinfo" or "https://www.googleapis.com/oauth2/v3/userinfo" or
                "https://openidconnect.googleapis.com/v1/userinfo" =>
                    "{\"id\":\"google-test-user\",\"sub\":\"google-test-user\",\"email\":\"same@example.test\",\"name\":\"Untrusted profile name\"}",
                "https://graph.microsoft.com/v1.0/me" => "{\"id\":\"microsoft-test-user\",\"mail\":\"same@example.test\",\"displayName\":\"Untrusted profile name\"}",
                "https://api.github.com/user" => "{\"id\":42,\"login\":\"untrusted-name\",\"email\":\"same@example.test\"}",
                "https://api.weixin.qq.com/sns/oauth2/access_token" =>
                    "{\"access_token\":\"test-access-token\",\"refresh_token\":\"test-refresh-token\",\"openid\":\"wechat-test-user\",\"expires_in\":7200,\"scope\":\"snsapi_login\"}",
                _ => throw new InvalidOperationException("Unexpected outbound request in isolated OAuth test.")
            };
            return Task.FromResult(Response(payload, WeChatTextPlain && target.Contains("weixin", StringComparison.Ordinal) ? "text/plain" : "application/json"));
        }

        private static HttpResponseMessage Response(string payload, string contentType = "application/json") => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, contentType)
        };
    }
}
