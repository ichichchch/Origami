var builder = WebApplication.CreateBuilder(args);
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
// Enums as strings keep /api/v1 payloads readable for hand-written mobile clients.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MarkdownService>();
// This host is anonymous and read-only: AdminAccess (required by ContentService) always observes
// an anonymous principal, so an API process can never pass an administrator check.
builder.Services.AddScoped<AuthenticationStateProvider, AnonymousAuthenticationStateProvider>();
builder.Services.AddOptions<IdentityOptions>();
builder.Services.AddScoped<AdminAccess>();
builder.Services.AddScoped<ContentService>();
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddHttpClient<OpenAiEmbeddingGateway>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<IEmbeddingGateway>(provider => provider.GetRequiredService<OpenAiEmbeddingGateway>());
builder.Services.AddScoped<SemanticSearchService>();
builder.Services.AddScoped<PublicSearchService>();
builder.Services.AddScoped<McpReadTools>();
builder.Services.AddHealthChecks().AddCheck<CmsHealthCheck>("database");
// Machine traffic (/api, /mcp) shares one provider-cost budget per source IP, as it did on the website host.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // A written body keeps the exception handler from rewriting the 429 into an HTML page.
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Too many requests.", context.HttpContext.RequestAborted);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
         || context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
            ? RateLimitPartition.GetFixedWindowLimiter(
                (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString(),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter("other"));
});
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly(typeof(McpReadTools).Assembly);

var app = builder.Build();

// Public errors never return exception details; validation messages are safe, anything else stays opaque.
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
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsync("Internal error.", context.RequestAborted);
    });
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    // Published content must vanish the moment it is revoked, so responses stay uncached.
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Pragma = "no-cache";
    await next(context);
});
app.UseRateLimiter();
app.MapMcp("/mcp");
app.MapApiV1();
app.MapHealthChecks("/health");
await app.RunAsync();
return 0;

internal sealed class AnonymousAuthenticationStateProvider : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
}

public partial class Program { protected Program() { } }
