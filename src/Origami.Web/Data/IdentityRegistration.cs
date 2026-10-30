namespace Origami.Web.Data;

public static class IdentityRegistration
{
    public static IServiceCollection AddCmsIdentity(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        }).AddIdentityCookies();

        services.AddIdentityCore<AdminUser>(options =>
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
            options.SignIn.RequireConfirmedAccount = false;
            options.SignIn.RequireConfirmedEmail = false;
        })
            .AddEntityFrameworkStores<CmsDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // 預設資料保護權杖有效期為一天；密碼重設郵件承諾 30 分鐘，必須在伺服器端同步強制。
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromMinutes(PasswordResetService.TokenMinutes));
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
            options.OnRefreshingPrincipal = context =>
            {
                if (LoginSessionLifetime.TryRead(context.CurrentPrincipal, out var expires) &&
                    context.NewPrincipal?.Identity is ClaimsIdentity identity)
                {
                    foreach (var claim in identity.FindAll(AccessClaims.SessionExpiresAt).ToArray()) identity.RemoveClaim(claim);
                    identity.AddClaim(LoginSessionLifetime.CreateClaim(expires));
                }
                return Task.CompletedTask;
            };
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, options =>
        {
            options.ExpireTimeSpan = WebSignInFlow.PendingLifetime;
            options.SlidingExpiration = false;
        });

        // 對 Identity 註冊的每個 cookie 套用傳輸保護，而非只保護登入 cookie。
        foreach (var scheme in new[]
        {
            IdentityConstants.ApplicationScheme, IdentityConstants.ExternalScheme,
            IdentityConstants.TwoFactorRememberMeScheme, IdentityConstants.TwoFactorUserIdScheme
        })
        {
            services.Configure<CookieAuthenticationOptions>(scheme, options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
            });
        }
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-Origami.Admin";
            options.Cookie.Path = "/";
            options.Cookie.IsEssential = true;
            options.LoginPath = "/account/login";
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsync("你已登入，但沒有此站點的操作權限。", context.HttpContext.RequestAborted);
            };
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                var requestServices = context.HttpContext.RequestServices;
                var valid = await AdminAccess.ValidatePrincipalAsync(
                    context.Principal,
                    requestServices.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
                    requestServices.GetRequiredService<IOptions<IdentityOptions>>().Value,
                    context.HttpContext.RequestAborted,
                    requestServices.GetRequiredService<TimeProvider>());
                if (!valid)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    return;
                }
                await SecurityStampValidator.ValidatePrincipalAsync(context);
                // Identity 的戳記驗證器會要求續發，即使已關閉滑動續期。
                context.ShouldRenew = false;
            };
        });

        services.AddAuthorization();
        services.AddAntiforgery();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddScoped<AdminAccess>();
        services.AddSingleton<AdminProvisioner>();
        return services;
    }

    public static IEndpointRouteBuilder MapCmsIdentity(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/logout", LogoutAsync).RequireAuthorization();
        endpoints.MapExternalLogins();
        return endpoints;
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<AdminUser> userManager,
        SignInManager<AdminUser> signInManager,
        IDbContextFactory<CmsDbContext> contextFactory,
        IOptions<IdentityOptions> identityOptions,
        TimeProvider clock)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("Invalid logout request. Reload the page and try again.");
        }

        try
        {
            if (!await AdminAccess.ValidatePrincipalAsync(
                context.User, contextFactory, identityOptions.Value, context.RequestAborted, clock))
            {
                return Results.Unauthorized();
            }

            // 這些管理員物件屬於 HTTP 請求範圍，絕不屬於互動電路。
            var user = await userManager.GetUserAsync(context.User);
            if (user is null || user.IsDisabled)
            {
                return Results.Unauthorized();
            }
            var result = await userManager.UpdateSecurityStampAsync(user);
            if (!result.Succeeded)
            {
                return Results.Problem("The session changed. Reload the page and try again.", statusCode: StatusCodes.Status409Conflict);
            }
        }
        catch (DbUpdateException)
        {
            return Results.Problem("Could not revoke sessions. Please try logging out again.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (DbException)
        {
            return Results.Problem("Could not revoke sessions. Please try logging out again.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // 新安全戳記確實寫入後才回報成功。
        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/");
    }
}
