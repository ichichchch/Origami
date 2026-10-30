namespace Origami.Web.Data;

public static class ExternalLoginEndpoints
{
    public static IEndpointRouteBuilder MapExternalLogins(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/account/external");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid form. Reload the page and try again."); }
            catch (Exception error) when (error is CmsException or DbException or DbUpdateException or HttpRequestException or UnauthorizedAccessException)
            {
                await context.HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                WebSignInFlow.ClearPending(context.HttpContext);
                return Results.LocalRedirect("/account/login?error=external");
            }
        });
        group.MapPost("/challenge", ChallengeAsync).AllowAnonymous();
        group.MapPost("/link", LinkAsync).RequireAuthorization();
        group.MapPost("/link/complete", CompleteLinkAsync).RequireAuthorization();
        group.MapPost("/unlink", UnlinkAsync).RequireAuthorization();
        group.MapGet("/complete", CompleteAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> ChallengeAsync(HttpContext context, IAntiforgery antiforgery,
        WebSignInFlow flow, ExternalProviderCatalog providers, SignInManager<AdminUser> signIn,
        SiteContext site, TimeProvider clock)
    {
        await antiforgery.ValidateRequestAsync(context);
        if (!flow.TryAcquire(context)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var provider = Single(form, "provider");
        var rememberValue = Single(form, "Input.Remember");
        if (provider is null || !providers.IsAllowed(provider, WebSignInFlow.Origin(context)) ||
            rememberValue is not (null or "false" or "true")) return Results.BadRequest("Invalid login request.");
        WebSignInFlow.ClearPending(context);
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        var properties = CreateProperties(context, signIn, site, clock, provider, "login", rememberValue == "true");
        return Results.Challenge(properties, [provider]);
    }

    private static async Task<IResult> LinkAsync(HttpContext context, IAntiforgery antiforgery,
        WebSignInFlow flow, ExternalProviderCatalog providers, SignInManager<AdminUser> signIn,
        SiteContext site, TimeProvider clock)
    {
        await antiforgery.ValidateRequestAsync(context);
        if (!flow.TryAcquire(context)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var provider = Single(form, "provider");
        if (provider is null || !providers.IsAllowed(provider, WebSignInFlow.Origin(context)))
            return Results.BadRequest("Invalid provider.");
        var user = await flow.ReauthenticateAsync(context, Single(form, "password") ?? "", Single(form, "code") ?? "");
        if (user is null) return Results.LocalRedirect("/account/external-logins?error=verification");
        WebSignInFlow.ClearPending(context);
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        return Results.Challenge(CreateProperties(context, signIn, site, clock, provider, "link", false, user), [provider]);
    }

    private static async Task<IResult> UnlinkAsync(HttpContext context, IAntiforgery antiforgery,
        WebSignInFlow flow, OrigamiClient api, SignInManager<AdminUser> signIn)
    {
        await antiforgery.ValidateRequestAsync(context);
        if (!flow.TryAcquire(context)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var provider = Single(form, "provider");
        if (provider is not ("Google" or "Microsoft" or "GitHub" or "WeChat")) return Results.BadRequest("Invalid provider.");
        if (await flow.ReauthenticateAsync(context, Single(form, "password") ?? "", Single(form, "code") ?? "") is null)
            return Results.LocalRedirect("/account/external-logins?error=verification");
        await api.UnlinkExternalLoginAsync(provider, context.RequestAborted);
        await signIn.SignOutAsync();
        WebSignInFlow.ClearPending(context);
        return Results.LocalRedirect("/account/login?error=changed");
    }

    private static async Task<IResult> CompleteAsync(HttpContext context, WebSignInFlow flow,
        SignInManager<AdminUser> signIn, ExternalProviderCatalog providers, SiteContext site, TimeProvider clock)
    {
        if (!flow.TryAcquire(context)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        var external = await ReadExternalAsync(context, signIn, providers, site, clock);
        if (external is null) return await FailAsync(context);
        if (external.Properties.Items["origami:purpose"] == "link")
            return Results.LocalRedirect("/account/external-logins?confirm=1");
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        WebSignInFlow.ClearPending(context);
        var user = await signIn.UserManager.FindByLoginAsync(external.Info.LoginProvider, external.Info.ProviderKey);
        if (user is null) return Results.LocalRedirect("/account/login?error=unlinked");
        var signedIn = await flow.BeginAsync(context, user, external.Properties.Items["origami:remember"] == "1",
            external.Info.LoginProvider, external.Info.ProviderKey);
        return Results.LocalRedirect(signedIn ? "/admin/posts" : "/account/login");
    }

    private static async Task<IResult> CompleteLinkAsync(HttpContext context, IAntiforgery antiforgery,
        WebSignInFlow flow, SignInManager<AdminUser> signIn, ExternalProviderCatalog providers,
        SiteContext site, TimeProvider clock, OrigamiClient api)
    {
        await antiforgery.ValidateRequestAsync(context);
        if (!flow.TryAcquire(context)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        var external = await ReadExternalAsync(context, signIn, providers, site, clock);
        if (external is null || external.Properties.Items["origami:purpose"] != "link") return await FailAsync(context);
        await api.LinkExternalLoginAsync(new(external.Info.LoginProvider, external.Info.ProviderKey), context.RequestAborted);
        await signIn.SignOutAsync();
        WebSignInFlow.ClearPending(context);
        return Results.LocalRedirect("/account/login?error=changed");
    }

    public static async Task<string?> GetPendingLinkProviderAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var external = await ReadExternalAsync(context, services.GetRequiredService<SignInManager<AdminUser>>(),
            services.GetRequiredService<ExternalProviderCatalog>(), services.GetRequiredService<SiteContext>(),
            services.GetRequiredService<TimeProvider>());
        return external?.Properties.Items["origami:purpose"] == "link" ? external.Info.LoginProvider : null;
    }

    private static AuthenticationProperties CreateProperties(HttpContext context, SignInManager<AdminUser> signIn,
        SiteContext site, TimeProvider clock, string provider, string purpose, bool remember, AdminUser? user = null)
    {
        var properties = signIn.ConfigureExternalAuthenticationProperties(provider, "/account/external/complete", user?.Id);
        var expires = clock.GetUtcNow().Add(WebSignInFlow.PendingLifetime);
        properties.IsPersistent = false;
        properties.AllowRefresh = false;
        properties.ExpiresUtc = expires;
        properties.Items["origami:provider"] = provider;
        properties.Items["origami:origin"] = WebSignInFlow.Origin(context);
        properties.Items["origami:site"] = site.SiteId.ToString("D");
        properties.Items["origami:purpose"] = purpose;
        properties.Items["origami:remember"] = remember ? "1" : "0";
        properties.Items["origami:proof-expiry"] = expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        if (user is not null)
        {
            properties.Items["origami:user"] = user.Id;
            properties.Items["origami:stamp"] = user.SecurityStamp;
        }
        return properties;
    }

    private static async Task<ExternalContext?> ReadExternalAsync(HttpContext context, SignInManager<AdminUser> signIn,
        ExternalProviderCatalog providers, SiteContext site, TimeProvider clock)
    {
        var ticket = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!ticket.Succeeded || ticket.Properties is not { } properties) return null;
        var items = properties.Items;
        if (!items.TryGetValue("origami:provider", out var provider) || provider is null ||
            !providers.IsAllowed(provider, WebSignInFlow.Origin(context)) ||
            !items.TryGetValue("origami:origin", out var origin) ||
            !string.Equals(origin, WebSignInFlow.Origin(context), StringComparison.OrdinalIgnoreCase) ||
            !items.TryGetValue("origami:site", out var siteId) || siteId != site.SiteId.ToString("D") ||
            !items.TryGetValue("origami:purpose", out var purpose) || purpose is not ("login" or "link") ||
            !items.TryGetValue("origami:remember", out var remember) || remember is not ("0" or "1") ||
            !items.TryGetValue("origami:proof-expiry", out var expiry) ||
            !long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= clock.GetUtcNow().ToUnixTimeSeconds()) return null;
        string? expectedUserId = null;
        if (purpose == "link")
        {
            if (!LoginSessionLifetime.IsValid(context.User, clock.GetUtcNow()) ||
                !items.TryGetValue("origami:user", out expectedUserId) || string.IsNullOrEmpty(expectedUserId) ||
                !items.TryGetValue("origami:stamp", out var stamp) || string.IsNullOrEmpty(stamp)) return null;
            var user = await signIn.UserManager.GetUserAsync(context.User);
            var principalStamp = context.User.FindFirstValue(signIn.Options.ClaimsIdentity.SecurityStampClaimType);
            if (user is null || user.IsDisabled || user.Id != expectedUserId || user.SecurityStamp != stamp ||
                principalStamp != stamp || await signIn.UserManager.IsLockedOutAsync(user)) return null;
        }
        var info = await signIn.GetExternalLoginInfoAsync(expectedUserId);
        return info is not null && info.LoginProvider == provider && !string.IsNullOrWhiteSpace(info.ProviderKey)
            ? new ExternalContext(info, properties) : null;
    }

    private static string? Single(IFormCollection form, string key) => form[key].Count == 1 ? form[key][0] : null;

    private static async Task<IResult> FailAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        WebSignInFlow.ClearPending(context);
        return Results.LocalRedirect("/account/login?error=external");
    }

    private sealed class ExternalContext(ExternalLoginInfo info, AuthenticationProperties properties)
    {
        public ExternalLoginInfo Info { get; } = info;
        public AuthenticationProperties Properties { get; } = properties;
    }
}
