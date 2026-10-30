namespace Origami.Api;

/// <summary>
/// Origami.Api 主機的服務註冊與請求管線組裝。
/// 頂層陳述句只保留啟動順序；分支密集的邏輯集中於此處的獨立方法，
/// 以維持每段流程的認知複雜度在門檻之內。
/// </summary>
public static class ApiHostPipeline
{
    /// <summary>讀取 CMS 連線字串；缺少設定時擲出可轉為友善訊息的例外。</summary>
    public static string ResolveConnectionString(WebApplicationBuilder builder) =>
        builder.Configuration.GetConnectionString("CMS")
        ?? throw new InvalidOperationException("缺少 ConnectionStrings__CMS 設定。");

    /// <summary>註冊主機層的服務、Identity、驗證、速率限制與 MCP 端點。</summary>
    public static void ConfigureServices(WebApplicationBuilder builder, string connection)
    {
        // 主機級主體上限是所有端點（含匿名 /mcp）的後盾；完整備份匯入可達
        // ArchiveMaxBytes，僅對匯入端點逐請求放寬（見 ApplyRequestLimitsAsync），端點本身仍自行檢查大小。
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = Limits.HostRequestMaxBytes);
        // 表單繫結另有多部分主體長度上限（預設 128MB），須與匯入放寬值一致，
        // 否則 128MB 以上的 ZIP 會先死在繫結層，輪不到端點自己的檢查。
        builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = Limits.ArchiveMaxBytes);
        builder.Services.AddScoped<SiteContext>();
        builder.Services.AddDbContextFactory<CmsDbContext>(options => options.UseNpgsql(connection, o => o.UseVector()),
            lifetime: ServiceLifetime.Scoped);
        // 列舉以字串輸出，讓 /api/v1 回應內容對手寫的行動用戶端保持易讀。
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<MarkdownService>();
        // Identity 僅供管理服務建立帳號；不註冊 Cookie、SignInManager 或背景工作。
        // 密碼與鎖定政策和 Web 登入主機一致。
        builder.Services.AddIdentityCore<AdminUser>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequiredUniqueChars = 4;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<CmsDbContext>();
        // 主機／憑證驗證只負責守門；Core 每次操作仍重驗使用者、戳記、站點角色與 scope。
        builder.Services.AddScoped<AuthenticationStateProvider, AdminPrincipalProvider>();
        builder.Services.AddAuthentication(ServiceTokenAuth.SchemeName)
            .AddScheme<ServiceTokenOptions, ServiceTokenHandler>(ServiceTokenAuth.SchemeName,
                options => options.Token = builder.Configuration["Api:ServiceToken"]);
        builder.Services.AddAuthorization(options => options.AddPolicy(ServiceTokenAuth.BffOnlyPolicy,
            policy => policy.RequireAuthenticatedUser().RequireClaim(AccessClaims.Bff, "true")));
        builder.Services.AddScoped<AdminAccess>();
        builder.Services.AddScoped<SiteAdministrationService>();
        // 與 Identity EF store 共用 AddDbContextFactory 註冊的 scoped CmsDbContext，確保綁定交易一致。
        builder.Services.AddScoped<ExternalLoginService>();
        builder.Services.AddScoped<ApiCredentialService>();
        builder.Services.AddScoped<ContentService>();
        builder.Services.AddScoped<ContentTypeService>();
        builder.Services.AddScoped<TaxonomyService>();
        builder.Services.AddScoped<NavigationService>();
        builder.Services.AddScoped<WxrImportService>();
        builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        builder.Services.AddScoped<CommentService>();
        builder.Services.AddScoped<SettingsService>();
        builder.Services.AddScoped<ExportService>();
        builder.Services.AddScoped<ImportService>();
        // 上傳檔案持久化到網站在 /media 提供服務的同一個站點儲存目錄。
        builder.Services.AddScoped<StoragePaths>();
        builder.Services.AddScoped<MediaService>();
        builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
        builder.Services.AddOptions<BackupOptions>().BindConfiguration("Backup");
        builder.Services.AddHttpClient("OrigamiWebhook");
        builder.Services.AddHttpClient("OrigamiWxrAttachment")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        builder.Services.AddScoped<WebhookService>();
        builder.Services.AddScoped<SiteHealthService>();
        builder.Services.AddHttpClient<OpenAiEmbeddingGateway>(client => client.Timeout = TimeSpan.FromSeconds(60));
        builder.Services.AddScoped<IEmbeddingGateway>(provider => provider.GetRequiredService<OpenAiEmbeddingGateway>());
        builder.Services.AddScoped<SemanticSearchService>();
        builder.Services.AddScoped<PublicSearchService>();
        builder.Services.AddScoped<McpReadTools>();
        builder.Services.AddSingleton<IAssistantChatClientFactory, AssistantChatClientFactory>();
        builder.Services.AddScoped<SiteAssistant>();
        builder.Services.AddHealthChecks().AddCheck<CmsHealthCheck>("database");
        // 機器流量（/api、/mcp）沿用網站主機的做法，依來源 IP 共用同一個供應商成本預算。
        ConfigureRateLimiter(builder.Services);
        builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly(typeof(McpReadTools).Assembly);
    }

    /// <summary>組裝請求管線與端點對應；中介軟體順序不可更動。</summary>
    public static void ConfigurePipeline(WebApplication app)
    {
        // 對外錯誤絕不回傳例外細節；驗證訊息可安全顯示，其餘一律保持不透明。
        app.UseExceptionHandler(error => error.Run(context =>
            WriteExceptionResponseAsync(context, context.Features.Get<IExceptionHandlerFeature>()?.Error)));
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.UseHttpsRedirection();
        app.Use(ApplyNoStoreHeaders);
        // 站點必須在認證（尤其 PAT）及任何站點資料查詢前確定；/health 不受此檢查影響。
        app.UseMiddleware<SiteResolutionMiddleware>();
        app.UseAuthentication();
        // 限流先於授權拒絕，避免無效 token 的 401／PAT 的 403 請求繞過計數。
        app.UseRateLimiter();
        app.Use(RejectFailedAuthenticationAsync);
        app.UseAuthorization();
        // 僅匯入端點放寬大小；密碼只接受 HTTPS 或同機 loopback 的可信 BFF 通道。
        app.Use(ApplyRequestLimitsAsync);
        app.MapMcp("/mcp");
        app.MapApiV1();
        app.MapAdminApiV1();
        app.MapContentTypeApiV1();
        app.MapAdminTaxonomyApiV1();
        app.MapAdminMediaApiV1();
        app.MapAdminSettingsApiV1();
        app.MapAdminTransferApiV1();
        app.MapTenancyApiV1();
        // API 參考文件（Scalar）直接讀取本機自建的 OpenAPI 契約，不另生一份規格。
        // /scalar 不屬租戶路徑，故不經站點解析、不受速率限制，與既有的 /openapi/v1.json 同級公開。
        app.MapScalarApiReference(options =>
        {
            options.WithTitle("Origami API 文档");
            options.WithOpenApiRoutePattern("/openapi/v1.json");
            options.WithLocalization(new ScalarLocalizationOptions { Locale = "zh-CN" });
        });
        app.MapHealthChecks("/health");
    }

    /// <summary>把例外對應為不洩漏細節的對外回應；可顯示的驗證訊息以 JSON 回傳。</summary>
    private static async Task WriteExceptionResponseAsync(HttpContext context, Exception? exception)
    {
        if (exception is CmsException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = exception.Message }, context.RequestAborted);
            return;
        }
        if (exception is AccessDeniedException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "你沒有此操作的權限。" }, context.RequestAborted);
            return;
        }
        // 無有效使用者／安全戳記或外部憑證已失效；不能把角色不足的 403 混成登出。
        if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "登入已失效，請重新登入。" }, context.RequestAborted);
            return;
        }
        // 繫結失敗（查詢或路由值缺少、無效）屬用戶端錯誤，不應回 500。
        if (exception is BadHttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "請求參數無效。" }, context.RequestAborted);
            return;
        }
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsync("Internal error.", context.RequestAborted);
    }

    /// <summary>回應一律不快取，避免已撤回內容被快取繼續提供。</summary>
    private static async Task ApplyNoStoreHeaders(HttpContext context, RequestDelegate next)
    {
        // 已發布內容一經撤回須立即不可見，因此回應一律不快取。
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";
        await next(context);
    }

    /// <summary>租戶路徑上明確失敗的 Bearer 不得降級為匿名存取。</summary>
    private static async Task RejectFailedAuthenticationAsync(HttpContext context, RequestDelegate next)
    {
        if (SiteResolutionMiddleware.IsTenantPath(context.Request.Path))
        {
            var authentication = await context.AuthenticateAsync(ServiceTokenAuth.SchemeName);
            // 處理器結果會快取；不重跑 PAT 查詢。明確失敗的 Bearer 也不能在公開路由降級為匿名。
            if (authentication.Failure is not null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "憑證無效或已失效。" }, context.RequestAborted);
                return;
            }
        }
        await next(context);
    }

    /// <summary>建立使用者的傳輸限制，以及匯入與批次媒體端點的主體長度放寬。</summary>
    private static async Task ApplyRequestLimitsAsync(HttpContext context, RequestDelegate next)
    {
        var trustedLocalBff = ServiceTokenAuth.IsBff(context.User) &&
            context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
        if (HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.Value?.TrimEnd('/').Equals("/api/v1/admin/users", StringComparison.OrdinalIgnoreCase) == true &&
            !context.Request.IsHttps && !trustedLocalBff)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "建立使用者必須透過 HTTPS 或同機 BFF 通道傳送。" }, context.RequestAborted);
            return;
        }
        if (context.Request.Path.StartsWithSegments("/api/v1/admin/transfer/import", StringComparison.OrdinalIgnoreCase))
            context.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = Limits.ArchiveMaxBytes;
        // 批次媒體上傳一次可達多個單檔上限；逐端點放寬，其餘請求仍受主機上限保護。
        if (context.Request.Path.StartsWithSegments("/api/v1/admin/media/batch", StringComparison.OrdinalIgnoreCase))
            context.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize =
                (long)Limits.MediaBatchMax * Limits.FileUploadBytes + 1_048_576;
        await next(context);
    }

    /// <summary>依來源 IP 與存取身分分區的速率限制；BFF 主機不受限流。</summary>
    private static void ConfigureRateLimiter(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // 先寫入回應主體，避免例外處理器把 429 改寫成 HTML 頁面。
            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsync("Too many requests.", context.HttpContext.RequestAborted);
            };
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained<HttpContext>(
                PartitionedRateLimiter.Create<HttpContext, string>(PartitionBySource),
                PartitionedRateLimiter.Create<HttpContext, string>(PartitionByCredential));
        });
    }

    private static RateLimitPartition<string> PartitionBySource(HttpContext context)
    {
        if (!SiteResolutionMiddleware.IsTenantPath(context.Request.Path))
            return RateLimitPartition.GetNoLimiter("other");
        // 只有 BFF 主機可免限流；PAT 即使屬於全站管理者也不能免除。
        if (ServiceTokenAuth.IsBff(context.User)) return RateLimitPartition.GetNoLimiter("bff");
        var ip = (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();
        if (context.User.Identity?.IsAuthenticated == true &&
            context.User.HasClaim(claim => claim.Type == AccessClaims.CredentialId))
            return RateLimitPartition.GetFixedWindowLimiter("credential-ip:" + ip,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) });
        // 未認證管理請求保留 600/分/IP，其餘機器請求維持 30/分/IP；失敗認證也計入。
        var isAdmin = context.Request.Path.StartsWithSegments("/api/v1/admin", StringComparison.OrdinalIgnoreCase);
        return RateLimitPartition.GetFixedWindowLimiter((isAdmin ? "admin:" : "public:") + ip,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = isAdmin ? 600 : 30, Window = TimeSpan.FromMinutes(1) });
    }

    private static RateLimitPartition<string> PartitionByCredential(HttpContext context)
    {
        if (!SiteResolutionMiddleware.IsTenantPath(context.Request.Path) || ServiceTokenAuth.IsBff(context.User))
            return RateLimitPartition.GetNoLimiter("other");
        var credentialId = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirst(AccessClaims.CredentialId)?.Value : null;
        // 只使用驗過的憑證識別，不以秘密或使用者輸入作為分區鍵。
        return credentialId is not null
            ? RateLimitPartition.GetFixedWindowLimiter("credential:" + credentialId,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter("anonymous");
    }
}
