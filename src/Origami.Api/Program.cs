var builder = WebApplication.CreateBuilder(args);
// 主機級主體上限是所有端點（含匿名 /mcp）的後盾；完整備份匯入可達
// ArchiveMaxBytes，僅對匯入端點逐請求放寬（見 app.Use 一節），端點本身仍自行檢查大小。
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = Limits.HostRequestMaxBytes);
// 表單繫結另有多部分主體長度上限（預設 128MB），須與匯入放寬值一致，
// 否則 128MB 以上的 ZIP 會先死在繫結層，輪不到端點自己的檢查。
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = Limits.ArchiveMaxBytes);
string connection;
try
{
    connection = builder.Configuration.GetConnectionString("CMS")
        ?? throw new InvalidOperationException("缺少 ConnectionStrings__CMS 設定。");
}
catch (InvalidOperationException error)
{
    // 缺少設定屬於維運層面的問題，不是傾印堆疊追蹤的理由。
    await Console.Error.WriteLineAsync(error.Message + " 請透過安全的外部設定提供，勿將連線密碼寫入原始碼。");
    return 1;
}
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
builder.Services.AddScoped<ApiCredentialService>();
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<TaxonomyService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<ImportService>();
// 上傳檔案持久化到網站在 /media 提供服務的同一個站點儲存目錄。
builder.Services.AddScoped<StoragePaths>();
builder.Services.AddScoped<MediaService>();
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddHttpClient<OpenAiEmbeddingGateway>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<IEmbeddingGateway>(provider => provider.GetRequiredService<OpenAiEmbeddingGateway>());
builder.Services.AddScoped<SemanticSearchService>();
builder.Services.AddScoped<PublicSearchService>();
builder.Services.AddScoped<McpReadTools>();
builder.Services.AddHealthChecks().AddCheck<CmsHealthCheck>("database");
// 機器流量（/api、/mcp）沿用網站主機的做法，依來源 IP 共用同一個供應商成本預算。
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // 先寫入回應主體，避免例外處理器把 429 改寫成 HTML 頁面。
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Too many requests.", context.HttpContext.RequestAborted);
    };
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained<HttpContext>(
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
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
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = isAdmin ? 600 : 30, Window = TimeSpan.FromMinutes(1)
                });
        }),
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
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
        }));
});
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly(typeof(McpReadTools).Assembly);

var app = builder.Build();

// 對外錯誤絕不回傳例外細節；驗證訊息可安全顯示，其餘一律保持不透明。
app.UseExceptionHandler(error =>
{
    error.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
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
    });
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    // 已發布內容一經撤回須立即不可見，因此回應一律不快取。
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Pragma = "no-cache";
    await next(context);
});
// 站點必須在認證（尤其 PAT）及任何站點資料查詢前確定；/health 不受此檢查影響。
app.UseMiddleware<SiteResolutionMiddleware>();
app.UseAuthentication();
// 限流先於授權拒絕，避免無效 token 的 401／PAT 的 403 請求繞過計數。
app.UseRateLimiter();
app.Use(async (context, next) =>
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
});
app.UseAuthorization();
// 僅匯入端點放寬大小；密碼只接受 HTTPS 或同機 loopback 的可信 BFF 通道。
app.Use(async (context, next) =>
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
    await next(context);
});
app.MapMcp("/mcp");
app.MapApiV1();
app.MapAdminApiV1();
app.MapAdminTaxonomyApiV1();
app.MapAdminMediaApiV1();
app.MapAdminSettingsApiV1();
app.MapAdminTransferApiV1();
app.MapTenancyApiV1();
app.MapHealthChecks("/health");
await app.RunAsync();
return 0;

public partial class Program { protected Program() { } }
