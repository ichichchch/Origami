var isCommand = args.Length > 0 && args[0] is "migrate" or "admin-init" or "admin-reset";
if (isCommand && args.Length != 1)
{
    await Console.Error.WriteLineAsync("管理命令不接受額外參數；請勿將密碼放入命令列。");
    return 2;
}
var builder = WebApplication.CreateBuilder(isCommand ? [] : args);
// 即使未發布的組建以 Production 環境執行，也載入組建時期的靜態資產。
builder.WebHost.UseStaticWebAssets();
string connection;
try
{
    connection = builder.Configuration.GetConnectionString("CMS")
        ?? throw new InvalidOperationException("缺少 ConnectionStrings__CMS 設定。");
}
catch (InvalidOperationException error)
{
    // 缺少設定屬於維運層面的問題，不因此輸出堆疊追蹤。
    await Console.Error.WriteLineAsync(error.Message + " 請透過安全的外部設定提供，勿將連線密碼寫入原始碼。");
    return 1;
}
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SiteResolveThrottle>();
builder.Services.AddSingleton<ContentUnlockThrottle>();
builder.Services.AddScoped<SiteContext>(provider =>
{
    var site = new SiteContext();
    if (provider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items[SiteResolver.ResolvedSiteKey] is SiteSummary resolved)
        site.SiteId = resolved.Id;
    return site;
});
builder.Services.AddDbContextFactory<CmsDbContext>(options => options.UseNpgsql(connection, o => o.UseVector()), ServiceLifetime.Scoped);
builder.Services.AddScoped<CircuitHandler, SiteCircuitHandler>();
// 編輯器 200,000 字的值經 JSON 轉義後可能膨脹六倍。
// 傳輸大小保持受限，同時仍允許服務層獨立驗證本文上限。
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 2 * 1024 * 1024);
builder.Services.AddCmsIdentity();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginThrottle>(provider => new LoginThrottle(provider.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<TaxonomyService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<MediaService>();
builder.Services.AddSingleton<MarkdownService>();
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddHttpClient<OpenAiEmbeddingGateway>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<IEmbeddingGateway>(provider => provider.GetRequiredService<OpenAiEmbeddingGateway>());
builder.Services.AddScoped<SemanticSearchService>();
builder.Services.AddScoped<PublicSearchService>();
// 管理助理重用 MCP 唯讀工具作為 function tools；只有 /mcp 端點搬到了 Origami.Api。
builder.Services.AddScoped<McpReadTools>();
builder.Services.AddSingleton<IAssistantChatClientFactory, AssistantChatClientFactory>();
builder.Services.AddScoped<SiteAssistant>();
builder.Services.AddScoped<AiStatusService>();
// 匯入匯出自 API-first 起經 OrigamiClient 走 Api 主機；本機不註冊 Import 服務
// （Export 服務僅供背景備份複用，AdminAccess 由 AddCmsIdentity 註冊）。
builder.Services.AddScoped<TwoFactorService>();
builder.Services.AddHealthChecks().AddCheck<CmsHealthCheck>("database");
builder.Services.AddHostedService<EmbeddingJobWorker>();
builder.Services.AddHostedService<ContentLifecycleWorker>();
builder.Services.AddHostedService<MediaVariantBackfillService>();
// 每日自動備份屬背景工作：與嵌入工作同樣留在網站主機，直接複用 Core
// 匯出服務讀取資料庫與媒體檔案；預設關閉，由 Backup 組態開啟。
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddOptions<BackupOptions>().BindConfiguration("Backup");
builder.Services.AddHostedService<BackupWorker>();
// 匿名公開搜尋對每個來源 IP 維持一份供應商成本預算；MCP 端點
// 現位於 Origami.Api 主機，並在該主機自帶速率限制器。
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // 寫入回應本文可避免狀態碼頁面中介軟體把 429 改寫成 HTML 頁面。
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Too many requests.", context.HttpContext.RequestAborted);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/search", StringComparison.OrdinalIgnoreCase)
            ? RateLimitPartition.GetFixedWindowLimiter(
                (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString(),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter("other"));
});
builder.Services.AddScoped<StoragePaths>();
// BFF 資料通道：服務憑證不進瀏覽器；解析主機的 client 獨立於租戶資料通道。
builder.Services.AddScoped<AdminSession>();
void ConfigureApiClient(HttpClient client)
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://127.0.0.1:5100");
    var serviceToken = builder.Configuration["Api:ServiceToken"];
    if (!string.IsNullOrWhiteSpace(serviceToken))
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
}
builder.Services.AddHttpClient<OrigamiClient>(ConfigureApiClient)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddHttpClient<SiteResolver>(client =>
    {
        ConfigureApiClient(client);
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
// 金鑰目錄維持全域，application name 保持舊值以相容既有 Cookie。
var keyStorage = new StoragePaths(builder.Configuration, builder.Environment);
var protection = builder.Services.AddDataProtection().SetApplicationName("Origami.SingleSite")
    .PersistKeysToFileSystem(new DirectoryInfo(keyStorage.Keys));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();

var app = builder.Build();
if (isCommand) return await RunCommandAsync(args, app);

// 公開錯誤絕不回傳例外細節，本機開發環境也不例外。
app.UseExceptionHandler("/Error", createScopeForErrors: true);
if (!app.Environment.IsDevelopment()) app.UseHsts();
// 轉址先於站點解析：未登錄主機的 HTTP 請求不必先付出解析成本，直接導向 HTTPS。
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Pragma = "no-cache";
    await next(context);
});
// 包含 /_blazor 的握手與 WebSocket；在認證、媒體與任何租戶服務之前解析。
// 每請求都會執行，因此 SiteResolver 內建短快取與每來源冷解析預算（見 SiteResolver.cs）。
app.Use(async (context, next) =>
{
    SiteSummary? site;
    try
    {
        site = await context.RequestServices.GetRequiredService<SiteResolver>()
            .ResolveAsync(context.Request.Host.Host, context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted);
    }
    catch (SiteResolutionException)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync("站點服務暫時無法使用，請稍後重試。", context.RequestAborted);
        return;
    }
    if (site is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("站點不存在或已停用。", context.RequestAborted);
        return;
    }
    context.Items[SiteResolver.ResolvedSiteKey] = site;
    context.RequestServices.GetRequiredService<SiteContext>().SiteId = site.Id;
    try { await next(context); }
    catch (AccessDeniedException) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("你已登入，但沒有此站點的操作權限。請聯絡站點管理員。", context.RequestAborted);
    }
    catch (UnauthorizedAccessException) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("登入或網站服務憑證已失效，請重新登入；若持續失敗請聯絡管理者。", context.RequestAborted);
    }
});
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();
app.MapStaticAssets();
app.MapCmsIdentity();
app.MapPublicEndpoints();
// 媒體仍公開，但只服務已核定站點的圖片目錄，絕不映射全域目錄。
app.MapGet("/media/{filename}", (string filename, StoragePaths paths) =>
{
    if (!ContentRules.IsSafeMediaFileName(filename) || filename.Contains("..", StringComparison.Ordinal) ||
        filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        return Results.NotFound();
    var contentType = Path.GetExtension(filename).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".jpg" => "image/jpeg", ".webp" => "image/webp", _ => null
    };
    var path = Path.Combine(paths.Images, filename);
    return contentType is not null && File.Exists(path) ? Results.File(path, contentType) : Results.NotFound();
});
// 瀏覽器依慣例請求這個網址；直接提供已設定的媒體資產，不經媒體庫查詢介面。
app.MapGet("/favicon.ico", async (MediaService media, CancellationToken ct) =>
{
    var icon = await media.GetSiteIconAsync(ct);
    return icon is { } file && File.Exists(file.Path) ? Results.File(file.Path, file.ContentType) : Results.NotFound();
});
// iOS 主畫面圖示慣例網址；規格要求 PNG，其他格式的網站圖示寧可 404 也不提供錯誤格式。
app.MapGet("/apple-touch-icon.png", async (MediaService media, CancellationToken ct) =>
{
    var icon = await media.GetSiteIconAsync(ct);
    return icon is { Path: var path, ContentType: "image/png" } && File.Exists(path) ? Results.File(path, "image/png") : Results.NotFound();
});
app.MapHealthChecks("/health");
// 匯出資料同樣經 Api（BFF 通道）；Api 端緩衝後一次輸出，失敗不會以 200 狀態串流出被截斷的檔案。
app.MapGet("/admin/export", async (OrigamiClient api, CancellationToken ct) =>
    Results.File(await api.ExportAsync(ct), "application/json", "origami-export.json"))
    .RequireAuthorization();
app.MapGet("/admin/export-archive", async (OrigamiClient api, CancellationToken ct) =>
    Results.File(await api.ExportArchiveAsync(ct), "application/zip", "origami-export.zip"))
    .RequireAuthorization();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
return 0;

static async Task<int> RunCommandAsync(string[] args, WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    if (args[0] != "migrate") return await AdminCommand.RunAsync(scope.ServiceProvider, args[0]);
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        await db.Database.MigrateAsync();
        Console.WriteLine("資料庫遷移已完成。");
        return 0;
    }
    catch
    {
        await Console.Error.WriteLineAsync("資料庫遷移失敗，請由伺服器端檢查資料庫連線及權限。");
        return 1;
    }
}

public partial class Program { protected Program() { } }
