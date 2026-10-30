namespace Origami.Web.Services;

internal static class ExternalOAuthSecurity
{
    internal const string CompletionPath = "/account/external/complete";
    internal const string FailureMessage = "External authentication failed.";
    internal static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(5);

    internal static bool IsBound(HttpContext context, string scheme, AuthenticationProperties? properties, string? returnUri)
    {
        if (properties is null || returnUri != CompletionPath || !context.Request.IsHttps || context.Request.PathBase.HasValue)
            return false;
        var origin = ExternalProviderCatalog.NormalizeOrigin($"{context.Request.Scheme}://{context.Request.Host}");
        var site = context.RequestServices.GetRequiredService<SiteContext>().SiteId;
        return origin is not null && site != Guid.Empty &&
            context.RequestServices.GetRequiredService<ExternalProviderCatalog>().IsAllowed(scheme, origin) &&
            properties.Items.TryGetValue("origami:provider", out var provider) && provider == scheme &&
            properties.Items.TryGetValue("origami:origin", out var boundOrigin) &&
                ExternalProviderCatalog.NormalizeOrigin(boundOrigin) == origin &&
            properties.Items.TryGetValue("origami:site", out var boundSite) && boundSite == site.ToString("D") &&
            properties.Items.TryGetValue("origami:purpose", out var purpose) && purpose is "login" or "link" &&
            properties.Items.TryGetValue("origami:remember", out var remember) && remember is "0" or "1";
    }

    internal static bool IsCurrent(OAuthOptions options, AuthenticationProperties properties)
    {
        var now = (options.TimeProvider ?? TimeProvider.System).GetUtcNow();
        return properties.IssuedUtc is { } issued && properties.ExpiresUtc is { } expires &&
            issued <= now && now < expires && expires - issued <= StateLifetime &&
            !properties.IsPersistent && properties.AllowRefresh == false;
    }

    internal static async Task ChallengeAsync(HttpContext context, string scheme, OAuthOptions options,
        AuthenticationProperties properties, Func<AuthenticationProperties, Task> challenge)
    {
        if (!IsBound(context, scheme, properties, properties.RedirectUri))
        {
            await RejectAsync(context);
            return;
        }
        var now = (options.TimeProvider ?? TimeProvider.System).GetUtcNow();
        properties.IsPersistent = false;
        properties.AllowRefresh = false;
        properties.IssuedUtc = now;
        properties.ExpiresUtc = now.Add(StateLifetime);
        await challenge(properties);
    }

    internal static async Task<HandleRequestResult> AuthenticateAsync(HttpContext context, string scheme,
        OAuthOptions options, Func<Task<HandleRequestResult>> authenticate)
    {
        try
        {
            var state = context.Request.Query["state"];
            if (state.Count != 1 || string.IsNullOrEmpty(state[0]) || state[0]!.Length > 16384)
                return HandleRequestResult.Fail(FailureMessage);
            if (context.Request.Query["code"].Count > 1 || context.Request.Query["error"].Count > 1)
                return HandleRequestResult.Fail(FailureMessage);
            var properties = options.StateDataFormat.Unprotect(state[0]);
            if (!IsBound(context, scheme, properties, properties?.RedirectUri) || !IsCurrent(options, properties!))
                return HandleRequestResult.Fail(FailureMessage);

            // 此處只加站點邊界檢查；仍由框架核對 correlation、state、授權碼及 PKCE。
            var result = await authenticate();
            return result.Failure is null ? result : HandleRequestResult.Fail(FailureMessage);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return HandleRequestResult.Handle();
        }
        catch (Exception)
        {
            // 遠端例外可能包含 token 回應或微信含 secret 的 URL，不能傳給框架的失敗日誌。
            return HandleRequestResult.Fail(FailureMessage);
        }
    }

    internal static ClaimsPrincipal MinimalPrincipal(ClaimsPrincipal? principal, string scheme)
    {
        var identifiers = principal?.FindAll(ClaimTypes.NameIdentifier).ToArray();
        if (identifiers is not { Length: 1 } || string.IsNullOrWhiteSpace(identifiers[0].Value) ||
            identifiers[0].Value.Length > 512 || identifiers[0].Value.Any(char.IsControl))
            throw new AuthenticationFailureException(FailureMessage);
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, identifiers[0].Value, ClaimValueTypes.String, scheme)], scheme));
    }

    internal static async Task RejectAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        context.Response.Redirect("/account/login?error=external");
    }
}

internal sealed class ExternalAuthenticationBackchannel : HttpClient
{
    public ExternalAuthenticationBackchannel() : base(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        // 不經 IHttpClientFactory 的 URL 日誌管線，尤其微信換碼 URL 本身含有秘密。
        Timeout = TimeSpan.FromSeconds(15);
        MaxResponseContentBufferSize = 1024 * 1024;
    }
}

// 框架的 Debug challenge 日誌含 Location／Set-Cookie；保留層級與事件 ID，但不轉送 payload 或例外。
internal sealed class ExternalAuthenticationLoggerFactory(ILoggerFactory inner) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new ExternalAuthenticationLogger(inner.CreateLogger(categoryName));
    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
    public void Dispose() { }

    private sealed class ExternalAuthenticationLogger(ILogger innerLogger) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => innerLogger.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => innerLogger.Log(logLevel, eventId,
                "External authentication event (sensitive details omitted).", null, static (message, _) => message);
    }
}
