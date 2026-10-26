var isCommand = args.Length > 0 && args[0] is "migrate" or "admin-init" or "admin-reset";
if (isCommand && args.Length != 1)
{
    await Console.Error.WriteLineAsync("管理命令不接受額外參數；請勿將密碼放入命令列。");
    return 2;
}
var builder = WebApplication.CreateBuilder(isCommand ? [] : args);
// Load build-time assets even when an unpublished build runs in Production.
builder.WebHost.UseStaticWebAssets();
string connection;
try
{
    connection = builder.Configuration.GetConnectionString("CMS")
        ?? throw new InvalidOperationException("缺少 ConnectionStrings__CMS 設定。");
}
catch (InvalidOperationException error)
{
    // A missing setting is an operator problem, not a reason to print a stack trace.
    await Console.Error.WriteLineAsync(error.Message + " 請透過安全的外部設定提供，勿將連線密碼寫入原始碼。");
    return 1;
}
builder.Services.AddDbContextFactory<CmsDbContext>(options => options.UseNpgsql(connection, o => o.UseVector()));
// A 200,000-character editor value may expand sixfold during JSON escaping.
// Keep transport bounded while allowing the service's independently validated body limit.
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
builder.Services.AddScoped<McpReadTools>();
builder.Services.AddSingleton<IAssistantChatClientFactory, AssistantChatClientFactory>();
builder.Services.AddScoped<SiteAssistant>();
builder.Services.AddScoped<AiStatusService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<TwoFactorService>();
builder.Services.AddHealthChecks().AddCheck<CmsHealthCheck>("database");
builder.Services.AddHostedService<EmbeddingJobWorker>();
builder.Services.AddHostedService<ContentLifecycleWorker>();
builder.Services.AddHostedService<MediaVariantBackfillService>();
// Anonymous MCP and public searches share one provider-cost budget per source IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // A written body keeps the status-code-pages middleware from rewriting the 429 into an HTML page.
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Too many requests.", context.HttpContext.RequestAborted);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        (context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase)
         || context.Request.Path.StartsWithSegments("/search", StringComparison.OrdinalIgnoreCase))
            ? RateLimitPartition.GetFixedWindowLimiter(
                (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString(),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter("other"));
});
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();
var storage = new StoragePaths(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(storage);
var protection = builder.Services.AddDataProtection().SetApplicationName("Origami.SingleSite")
    .PersistKeysToFileSystem(new DirectoryInfo(storage.Keys));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();

var app = builder.Build();
if (isCommand) return await RunCommandAsync(args, app);

// Public errors never return exception details, including in local development.
app.UseExceptionHandler("/Error", createScopeForErrors: true);
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
var contentTypes = new FileExtensionContentTypeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [".png"] = "image/png", [".jpg"] = "image/jpeg", [".webp"] = "image/webp"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storage.Images), RequestPath = "/media",
    ContentTypeProvider = contentTypes, ServeUnknownFileTypes = false
});
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();
app.MapStaticAssets();
app.MapCmsIdentity();
app.MapPublicEndpoints();
// Browsers request this URL by convention; the configured media asset is served without any library query UI.
app.MapGet("/favicon.ico", async (MediaService media, CancellationToken ct) =>
{
    var icon = await media.GetSiteIconAsync(ct);
    return icon is { } file && File.Exists(file.Path) ? Results.File(file.Path, file.ContentType) : Results.NotFound();
});
app.MapHealthChecks("/health");
// The buffer keeps a failed export from streaming a truncated file with a 200 status.
app.MapGet("/admin/export", async (ExportService export, CancellationToken ct) =>
{
    using var buffer = new MemoryStream();
    await export.WriteAsync(buffer, ct);
    return Results.File(buffer.ToArray(), "application/json", "origami-export.json");
}).RequireAuthorization();
app.MapMcp("/mcp");
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
