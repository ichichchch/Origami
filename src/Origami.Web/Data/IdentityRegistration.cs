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
            options.Password.RequiredLength = 12;
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

        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.Zero);

        // Apply transport protections to every cookie registered by Identity, not just login.
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
            options.AccessDeniedPath = "/account/login";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = async context =>
            {
                var requestServices = context.HttpContext.RequestServices;
                // Explicit validation also covers requests within the cookie's issue-time tick.
                var valid = await AdminAccess.ValidatePrincipalAsync(
                    context.Principal,
                    requestServices.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
                    requestServices.GetRequiredService<IOptions<IdentityOptions>>().Value,
                    context.HttpContext.RequestAborted);
                if (!valid)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    return;
                }
                // Keep the official Identity validator/claims refresh as well.
                await SecurityStampValidator.ValidatePrincipalAsync(context);
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
        return endpoints;
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<AdminUser> userManager,
        SignInManager<AdminUser> signInManager,
        IDbContextFactory<CmsDbContext> contextFactory,
        IOptions<IdentityOptions> identityOptions)
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
                context.User, contextFactory, identityOptions.Value, context.RequestAborted))
            {
                return Results.Unauthorized();
            }

            // These managers belong to the HTTP request, never to an interactive circuit.
            var user = await userManager.GetUserAsync(context.User);
            if (user is null || user.SingletonKey != 1)
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

        // Only report success once the new stamp has been persisted.
        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/");
    }
}
