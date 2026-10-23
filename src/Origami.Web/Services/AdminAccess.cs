namespace Origami.Web.Services;

public sealed class AdminAccess(
    AuthenticationStateProvider authenticationStateProvider,
    IDbContextFactory<CmsDbContext> contextFactory,
    IOptions<IdentityOptions> identityOptions)
{
    public async Task RequireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Never accept a principal from a component, request payload, or caller.
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (!await ValidatePrincipalAsync(state.User, contextFactory, identityOptions.Value, cancellationToken))
        {
            throw new UnauthorizedAccessException("Your administrator session has expired. Sign in again.");
        }
    }

    public static async Task<bool> ValidatePrincipalAsync(
        ClaimsPrincipal? principal,
        IDbContextFactory<CmsDbContext> contextFactory,
        IdentityOptions identityOptions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var userId = principal.FindFirstValue(identityOptions.ClaimsIdentity.UserIdClaimType);
        var stamp = principal.FindFirstValue(identityOptions.ClaimsIdentity.SecurityStampClaimType);
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp))
        {
            return false;
        }

        // A circuit must not retain a DbContext or trust its last periodic validation.
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.AsNoTracking()
            .Where(user => user.Id == userId && user.SingletonKey == 1)
            .Select(user => new { user.SecurityStamp })
            .SingleOrDefaultAsync(cancellationToken);

        return user is not null && !string.IsNullOrEmpty(user.SecurityStamp)
            && string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal);
    }
}
