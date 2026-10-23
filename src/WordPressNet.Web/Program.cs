using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using WordPressNet.Web.Components;
using WordPressNet.Web.Data;
using WordPressNet.Web.Services;

var isCommand = args.Length > 0 && args[0] is "migrate" or "admin-init" or "admin-reset";
if (isCommand && args.Length != 1)
{
    Console.Error.WriteLine("管理命令不接受額外參數；請勿將密碼放入命令列。");
    return 2;
}
var builder = WebApplication.CreateBuilder(isCommand ? [] : args);
// Load build-time assets even when an unpublished build runs in Production.
builder.WebHost.UseStaticWebAssets();
string connection;
try
{
    connection = builder.Configuration.GetConnectionString("Cms")
        ?? throw new InvalidOperationException("缺少 ConnectionStrings__Cms 設定。");
}
catch (InvalidOperationException error)
{
    // A missing setting is an operator problem, not a reason to print a stack trace.
    Console.Error.WriteLine(error.Message + " 請透過安全的外部設定提供，勿將連線密碼寫入原始碼。");
    return 1;
}
builder.Services.AddDbContextFactory<CmsDbContext>(options => options.UseNpgsql(connection));
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
var storage = new StoragePaths(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(storage);
var protection = builder.Services.AddDataProtection().SetApplicationName("WordPressNet.SingleSite")
    .PersistKeysToFileSystem(new DirectoryInfo(storage.Keys));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetSlidingWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 4, QueueLimit = 0
        }));
});

var app = builder.Build();
if (isCommand)
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
        Console.Error.WriteLine("資料庫遷移失敗，請由伺服器端檢查資料庫連線及權限。");
        return 1;
    }
}

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
app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapCmsIdentity();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
return 0;

public partial class Program;
