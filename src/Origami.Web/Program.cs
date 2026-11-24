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
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture(UiTextLanguages.Default)
        .AddSupportedCultures(UiTextLanguages.Supported.ToArray())
        .AddSupportedUICultures(UiTextLanguages.Supported.ToArray());
    options.RequestCultureProviders = [new CustomRequestCultureProvider(async context =>
    {
        // 語言優先序：URL 查詢參數 > 裝置 Cookie > 已登入使用者偏好 > 預設值。
        // 本中介註冊於驗證之後（見 UseRequestLocalization 位置），因此可讀取已驗證主體；
        // 使用者偏好查詢走主機層 DbContextFactory 先例並帶短快取。
        var culture = UiTextLanguages.Normalize(context.Request.Query["lang"].ToString())
            ?? UiTextLanguages.Normalize(context.Request.Cookies[UiTextLanguages.CookieName]);
        if (culture is null)
        {
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
                culture = await context.RequestServices.GetRequiredService<UserLanguagePreferenceReader>()
                    .GetAsync(userId, context.RequestAborted);
        }
        // 最後退回站點預設介面語言；站點未設定時沿用產品預設。
        culture ??= await context.RequestServices.GetRequiredService<SiteDefaultLanguageReader>()
            .GetAsync(context, context.RequestAborted);
        return new ProviderCultureResult(culture ?? UiTextLanguages.Default);
    })];
});
// AddDbContextFactory 以 Scoped 註冊工廠；Singleton 讀取器會形成 captive dependency，
// 啟動驗證即失敗。快取經單例 IMemoryCache 共享，Scoped 註冊不影響跨請求快取與失效。
builder.Services.AddScoped<UserLanguagePreferenceReader>();
builder.Services.AddScoped<SiteDefaultLanguageReader>();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat([
        "application/xml", "application/problem+json", "image/svg+xml"
    ]).ToArray();
});
builder.Services.AddSingleton<SiteResolveThrottle>();
builder.Services.AddSingleton<ContentUnlockThrottle>();
builder.Services.AddSingleton<CommentThrottle>();
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
builder.Services.AddExternalAuthentication(builder.Configuration);
builder.Services.AddScoped<WebSignInFlow>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginThrottle>(provider => new LoginThrottle(provider.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<TaxonomyService>();
builder.Services.AddScoped<NavigationService>();
builder.Services.AddScoped<WidgetService>();
builder.Services.AddScoped<WxrImportService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<CommentService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<CmsEventBus>();
builder.Services.AddScoped<StorageMutationCoordinator>();
builder.Services.AddScoped<MediaService>();
builder.Services.AddSingleton<MarkdownService>();
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddHttpClient("OrigamiWebhook")
    .ConfigurePrimaryHttpMessageHandler(() => new PublicNetworkHttpHandler());
builder.Services.AddHttpClient("OrigamiWidgetFeed", client => client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new PublicNetworkHttpHandler());
builder.Services.AddHttpClient("OrigamiWxrAttachment")
    .ConfigurePrimaryHttpMessageHandler(() => new PublicNetworkHttpHandler());
// 從網址匯入媒體（sideload）的下載通道：與 Api 主機同一組邊界（30 秒逾時、不跟隨轉址、不帶 cookie）。
builder.Services.AddHttpClient("OrigamiMediaSideload", client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new PublicNetworkHttpHandler());
builder.Services.AddScoped<WebhookService>();
builder.Services.AddScoped<IndexNowService>();
builder.Services.AddScoped<SiteHealthService>();
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
builder.Services.AddScoped<LoginSessionService>();
builder.Services.AddHealthChecks().AddCheck<CmsHealthCheck>("database");
builder.Services.AddHostedService<EmbeddingJobWorker>();
builder.Services.AddHostedService<ContentLifecycleWorker>();
builder.Services.AddHostedService<MediaVariantBackfillService>();
// 每日自動備份屬背景工作：與嵌入工作同樣留在網站主機，直接複用 Core
// 匯出服務讀取資料庫與媒體檔案；預設關閉，由 Backup 組態開啟。
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddOptions<BackupOptions>().BindConfiguration("Backup");
// 匿名頁面輸出快取：僅無 Cookie 的 GET/HEAD 白名單路徑（見 PublicPageCachePolicy）。
// Web 與 Api 是不同進程（BFF），發布事件只在 Api 進程內分發、沒有跨進程失效信號，
// 因此以短 TTL（3 分鐘）約束新鮮度；含防偽表單的詳情頁不進快取，改走 ETag 條件請求。
builder.Services.AddOutputCache(options => options.AddBasePolicy(policy => policy
    .With(PublicPageCachePolicy.Cacheable)
    .Expire(TimeSpan.FromMinutes(3))

    .SetVaryByHost(true)
    .SetVaryByQuery("*")));
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
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
builder.Services.AddScoped<PublicSettingsProvider>();
builder.Services.AddHttpClient<SiteResolver>(client =>
    {
        ConfigureApiClient(client);
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
// 金鑰目錄維持全域，application name 保持舊值以相容既有 Cookie。
var keyStorage = new StoragePaths(builder.Configuration, builder.Environment);
var protection = builder.Services.AddDataProtection().SetApplicationName("Origami.SingleSite")
    .PersistKeysToFileSystem(new DirectoryInfo(keyStorage.Keys));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();

var app = builder.Build();
if (isCommand) return await RunCommandAsync(args, app);

// 語言 Cookie 種植：?lang= 同時記住裝置選擇（365 天）。
app.Use(async (context, next) =>
{
    var requested = UiTextLanguages.Normalize(context.Request.Query["lang"].ToString());
    if (requested is not null)
        context.Response.Cookies.Append(UiTextLanguages.CookieName, requested, new CookieOptions
        {
            Path = "/", MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps, HttpOnly = true
        });
    await next(context);
});

// 公開錯誤絕不回傳例外細節，本機開發環境也不例外。
app.UseExceptionHandler("/Error", createScopeForErrors: true);
if (!app.Environment.IsDevelopment()) app.UseHsts();
// 轉址先於站點解析：未登錄主機的 HTTP 請求不必先付出解析成本，直接導向 HTTPS。
app.UseHttpsRedirection();
app.UseResponseCompression();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; " +
        "script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self' data:; media-src 'self' blob:; worker-src 'self' blob:; connect-src 'self' ws: wss:; " +
        "frame-src https://www.youtube-nocookie.com https://player.vimeo.com https://player.bilibili.com";
    // 靜態資產由端點指紋決定快取；動態回應一律 no-store。
    if (context.GetEndpoint()?.Metadata.GetMetadata<StaticAssetDescriptor>() is null)
    {
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
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
        await context.Response.WriteAsync(UiText.T("Common.SiteUnavailable", "站點服務暫時無法使用，請稍後重試。"), context.RequestAborted);
        return;
    }
    if (site is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync(UiText.T("Common.SiteMissing", "站點不存在或已停用。"), context.RequestAborted);
        return;
    }
    context.Items[SiteResolver.ResolvedSiteKey] = site;
    context.RequestServices.GetRequiredService<SiteContext>().SiteId = site.Id;
    try { await next(context); }
    catch (AccessDeniedException) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync(UiText.T("Common.AccessDenied", "你已登入，但沒有此站點的操作權限。請聯絡站點管理員。"), context.RequestAborted);
    }
    catch (UnauthorizedAccessException) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync(UiText.T("Common.CredentialsExpired", "登入或網站服務憑證已失效，請重新登入；若持續失敗請聯絡管理者。"), context.RequestAborted);
    }
});
// 搜尋引擎可見度：Static SSR 的 layout 層無法輸出 head meta，改以回應標頭涵蓋公開頁面。
// 標頭附加延後到回應啟動前且僅對 HTML 生效——靜態資源（css/js/媒體/favicon）不必付出
// Api 設定讀取的成本；設定經 PublicSettingsProvider 每請求快取，與版面共用同一次讀取，
// 因此關閉開關後立即生效。Api 讀取失敗時略過標頭，不讓可見度檢查拖垮任何回應。
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/_blazor")
        && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
    {
        context.Response.OnStarting(async () =>
        {
            if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) != true)
                return;
            try
            {
                var settings = await context.RequestServices.GetRequiredService<PublicSettingsProvider>()
                    .GetAsync(context.RequestAborted);
                if (settings.SearchEngineBlocked)
                    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception) { /* Api 不可用時不附加標頭；頁面本身另有降級處理。 */ }
        });
    }
    await next(context);
});
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
// 要求文化放在驗證之後：使用者介面語言偏好需要已驗證主體；前面各中介（安全標頭、站點解析、
// 可見度標頭）均不消費 CurrentCulture，順序調整不影響行為。
app.UseRequestLocalization();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();
// 輸出快取放在限流之後：命中同樣計入限流；詳情頁另以 ETag/304 支援條件請求。
app.UseOutputCache();
app.UseMiddleware<DetailPageEtagMiddleware>();
var staticAssets = app.MapStaticAssets()
    .WithMetadata(new StaticAssetResponseHeader("X-Content-Type-Options", "nosniff"));
staticAssets.Add(builder =>
{
    if (builder.Metadata.OfType<StaticAssetDescriptor>().SingleOrDefault()?.Properties.Any(x => x.Name == "fingerprint") != true) return;
    var next = builder.RequestDelegate!;
    builder.RequestDelegate = context =>
    {
        // 端點內建快取標頭較晚覆寫，於 OnStarting 依指紋重新指定。
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Task.CompletedTask;
        });
        return next(context);
    };
});
app.MapCmsIdentity();
app.MapPublicEndpoints();
// 媒體仍公開，但只服務已核定站點的圖片目錄，絕不映射全域目錄。
app.MapMethods("/media/{filename}", ["GET", "HEAD"], (string filename, StoragePaths paths, HttpContext context) =>
{
    if (!ContentRules.IsSafeMediaFileNameAny(filename) || filename.Contains("..", StringComparison.Ordinal) ||
        filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        return Results.NotFound();
    // 內容類型由副檔名白名單決定；未知副檔名一律 404，不猜測型別。
    var (contentType, disposition) = Path.GetExtension(filename).ToLowerInvariant() switch
    {
        ".png" => ("image/png", "inline"),
        ".jpg" => ("image/jpeg", "inline"),
        ".webp" => ("image/webp", "inline"),
        // 文件（PDF／XLSX／CSV）以附件下載，避免同源內嵌腳本風險；音訊影片可內嵌播放。
        ".pdf" => ("application/pdf", "attachment"),
        ".xlsx" => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "attachment"),
        ".csv" => ("text/csv", "attachment"),
        ".mp3" => ("audio/mpeg", "inline"),
        ".m4a" => ("audio/mp4", "inline"),
        ".ogg" => ("audio/ogg", "inline"),
        ".wav" => ("audio/wav", "inline"),
        ".mp4" => ("video/mp4", "inline"),
        ".webm" => ("video/webm", "inline"),
        _ => (null, null)
    };
    if (contentType is null) return Results.NotFound();
    var path = File.Exists(Path.Combine(paths.Images, filename))
        ? Path.Combine(paths.Images, filename)
        : Path.Combine(paths.Files, filename);
    if (!File.Exists(path)) return Results.NotFound();
    // 上傳檔名含隨機識別且不覆寫；變體亦同，可長期快取。設定型 favicon 不走此規則。
    context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    context.Response.Headers.Remove("Pragma");
    return Results.File(path, contentType, fileDownloadName: disposition == "attachment" ? filename : null,
        lastModified: File.GetLastWriteTimeUtc(path), enableRangeProcessing: true);
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
app.MapGet("/admin/export", async (OrigamiClient api, HttpContext http, CancellationToken ct) =>
{
    var kind = http.Request.Query["kind"].ToString();
    var category = http.Request.Query["category"].ToString();
    return Results.File(await api.ExportAsync(ct, kind.Length == 0 ? null : kind, category.Length == 0 ? null : category),
        "application/json", "origami-export.json");
})
    .RequireAuthorization();
app.MapGet("/admin/export-archive", async (OrigamiClient api, CancellationToken ct) =>
    Results.File(await api.ExportArchiveAsync(ct), "application/zip", "origami-export.zip"))
    .RequireAuthorization();
app.MapGet("/admin/export-wxr", async (OrigamiClient api, CancellationToken ct) =>
    Results.File(await api.ExportWxrAsync(ct), "application/xml", "origami-wxr-export.xml"))
    .RequireAuthorization();
// 受控包匯出：可分享的 ZIP（僅 manifest.json），供其他站點匯入。
app.MapGet("/admin/marketplace/package-export/{id:guid}", async (OrigamiClient api, Guid id, HttpContext http, CancellationToken ct) =>
{
    var raw = http.Request.Query["version"].ToString();
    if (!Guid.TryParse(raw, out var version)) return Results.BadRequest(new { message = "參數 version 無效。" });
    return Results.File(await api.ExportPackageAsync(id, version, ct), "application/zip", "origami-package.zip");
}).RequireAuthorization();
app.MapGet("/admin/personal-data-export", async (OrigamiClient api, CancellationToken ct) =>
{
    var data = await api.ExportMyPersonalDataAsync(ct);
    var json = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    return Results.File(json, "application/json", "origami-personal-data.json");
}).RequireAuthorization();
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
