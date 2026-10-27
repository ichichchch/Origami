var builder = WebApplication.CreateBuilder(args);
// One host-wide cap keeps oversized imports from streaming forever; endpoints gate sizes themselves.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = Limits.ImportMaxBytes);
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
builder.Services.AddDbContextFactory<CmsDbContext>(options => options.UseNpgsql(connection, o => o.UseVector()));
// 列舉以字串輸出，讓 /api/v1 回應內容對手寫的行動用戶端保持易讀。
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MarkdownService>();
// BFF 寫入授權：服務 token 負責守門，下方 AdminAccess 則在每次寫入操作時
// 重新向資料庫驗證管理員的使用者 id 與安全戳記。
builder.Services.AddScoped<AuthenticationStateProvider, AdminPrincipalProvider>();
builder.Services.AddOptions<IdentityOptions>();
builder.Services.AddAuthentication(ServiceTokenAuth.SchemeName)
    .AddScheme<ServiceTokenOptions, ServiceTokenHandler>(ServiceTokenAuth.SchemeName,
        options => options.Token = builder.Configuration["Api:ServiceToken"]);
builder.Services.AddAuthorization();
builder.Services.AddScoped<AdminAccess>();
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<TaxonomyService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<ImportService>();
// 上傳檔案持久化到網站在 /media 提供服務的同一個儲存目錄。
var storage = new StoragePaths(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(storage);
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
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api/v1/admin", StringComparison.OrdinalIgnoreCase)
            // 網站主機代表單一管理員爆量呼叫，因此享有獨立且較寬鬆的預算。
            ? RateLimitPartition.GetFixedWindowLimiter(
                "admin:" + (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString(),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) })
            : (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
               || context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
                ? RateLimitPartition.GetFixedWindowLimiter(
                    (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString(),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) })
                : RateLimitPartition.GetNoLimiter("other"));
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
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapMcp("/mcp");
app.MapApiV1();
app.MapAdminApiV1();
app.MapAdminTaxonomyApiV1();
app.MapAdminMediaApiV1();
app.MapAdminSettingsApiV1();
app.MapAdminTransferApiV1();
app.MapHealthChecks("/health");
await app.RunAsync();
return 0;

public partial class Program { protected Program() { } }
