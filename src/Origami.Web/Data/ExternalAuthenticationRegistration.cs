namespace Origami.Web.Data;

public static class ExternalAuthenticationRegistration
{
    public static IServiceCollection AddExternalAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var catalog = new ExternalProviderCatalog(configuration);
        services.AddSingleton(catalog);
        services.AddSingleton<ExternalAuthenticationBackchannel>();
        var authentication = services.AddAuthentication();
        foreach (var provider in catalog.Providers)
        {
            switch (provider.Name)
            {
                case "Google":
                    // 與官方 AddGoogle 相同的 OAuth 註冊，只在原 handler 外加站點邊界檢查。
                    AddProvider<GoogleOptions, ExternalGoogleHandler>(authentication, provider, options =>
                    {
                        options.CallbackPath = "/signin-google";
                        options.Scope.Clear();
                        options.Scope.Add("openid");
                        options.UsePkce = true;
                        options.AccessType = "online";
                    });
                    break;
                case "Microsoft":
                    AddProvider<MicrosoftAccountOptions, ExternalMicrosoftHandler>(authentication, provider, options =>
                    {
                        options.CallbackPath = "/signin-microsoft";
                        options.Scope.Clear();
                        options.Scope.Add("https://graph.microsoft.com/user.read");
                        options.UserInformationEndpoint = "https://graph.microsoft.com/v1.0/me?$select=id";
                        options.UsePkce = true;
                    });
                    break;
                case "GitHub":
                    AddProvider<OAuthOptions, ExternalOAuthHandler<OAuthOptions>>(authentication, provider, options =>
                    {
                        options.CallbackPath = "/signin-github";
                        options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
                        options.TokenEndpoint = "https://github.com/login/oauth/access_token";
                        options.UserInformationEndpoint = "https://api.github.com/user";
                        options.Scope.Clear();
                        options.UsePkce = true;
                        options.Events.OnCreatingTicket = CreateGitHubTicketAsync;
                    });
                    break;
                case "WeChat":
                    AddProvider<WeChatOAuthOptions, WeChatOAuthHandler>(authentication, provider, _ => { });
                    break;
            }
        }
        return services;
    }

    private static void AddProvider<TOptions, THandler>(AuthenticationBuilder authentication,
        ExternalProviderConfiguration provider, Action<TOptions> configure)
        where TOptions : OAuthOptions, new()
        where THandler : OAuthHandler<TOptions>
    {
        authentication.AddOAuth<TOptions, THandler>(provider.Name, provider.Name, _ => { });
        authentication.Services.AddOptions<TOptions>(provider.Name)
            .Configure<ExternalAuthenticationBackchannel>((options, backchannel) =>
            {
                options.ClientId = provider.ClientId;
                options.ClientSecret = provider.ClientSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.SaveTokens = false;
                options.RemoteAuthenticationTimeout = ExternalOAuthSecurity.StateLifetime;
                options.BackchannelTimeout = backchannel.Timeout;
                options.Backchannel = backchannel;
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.CorrelationCookie.SameSite = SameSiteMode.None;
                options.CorrelationCookie.IsEssential = true;
                configure(options);
                AttachBoundaryEvents(options);
            });
    }

    private static void AttachBoundaryEvents(OAuthOptions options)
    {
        var creatingTicket = options.Events.OnCreatingTicket;
        options.Events.OnCreatingTicket = async context =>
        {
            if (!ExternalOAuthSecurity.IsBound(context.HttpContext, context.Scheme.Name, context.Properties, context.Properties.RedirectUri) ||
                !ExternalOAuthSecurity.IsCurrent(options, context.Properties))
                throw new AuthenticationFailureException(ExternalOAuthSecurity.FailureMessage);
            await creatingTicket(context);
            context.Principal = ExternalOAuthSecurity.MinimalPrincipal(context.Principal, context.Scheme.Name);
        };

        var ticketReceived = options.Events.OnTicketReceived;
        options.Events.OnTicketReceived = async context =>
        {
            try
            {
                // 框架在此事件前將 Properties.RedirectUri 搬至 ReturnUri，不可把 null 誤判成遺失 state。
                if (context.Properties is not { } properties ||
                    !ExternalOAuthSecurity.IsBound(context.HttpContext, context.Scheme.Name, properties, context.ReturnUri) ||
                    !ExternalOAuthSecurity.IsCurrent(options, properties))
                    throw new AuthenticationFailureException(ExternalOAuthSecurity.FailureMessage);
                await ticketReceived(context);
                if (context.Result?.Handled == true) return;
                if (context.Result is not null)
                    throw new AuthenticationFailureException(ExternalOAuthSecurity.FailureMessage);
                context.Principal = ExternalOAuthSecurity.MinimalPrincipal(context.Principal, context.Scheme.Name);
                properties.StoreTokens([]);
                context.ReturnUri = ExternalOAuthSecurity.CompletionPath;
            }
            catch (Exception)
            {
                // 此事件不在框架 RemoteFailure 的 try 範圍內，必須自行轉為固定失敗導向。
                await ExternalOAuthSecurity.RejectAsync(context.HttpContext);
                context.HandleResponse();
            }
        };
        options.Events.OnRemoteFailure = async context =>
        {
            await ExternalOAuthSecurity.RejectAsync(context.HttpContext);
            context.HandleResponse();
        };
    }

    private static async Task CreateGitHubTicketAsync(OAuthCreatingTicketContext context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Origami");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode) throw new AuthenticationFailureException(ExternalOAuthSecurity.FailureMessage);
        using var profile = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        if (profile.RootElement.ValueKind != JsonValueKind.Object ||
            !profile.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number ||
            !id.TryGetInt64(out var identifier) || identifier <= 0)
            throw new AuthenticationFailureException(ExternalOAuthSecurity.FailureMessage);
        context.Identity!.AddClaim(new Claim(ClaimTypes.NameIdentifier, identifier.ToString(CultureInfo.InvariantCulture),
            ClaimValueTypes.String, context.Scheme.Name));
    }
}
