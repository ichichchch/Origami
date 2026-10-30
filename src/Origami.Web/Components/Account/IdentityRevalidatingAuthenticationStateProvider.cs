namespace Origami.Web.Components.Account;

public sealed class IdentityRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IDbContextFactory<CmsDbContext> contextFactory,
    IOptions<IdentityOptions> identityOptions,
    TimeProvider clock)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        AdminAccess.ValidatePrincipalAsync(
            authenticationState.User, contextFactory, identityOptions.Value, cancellationToken, clock);
}
