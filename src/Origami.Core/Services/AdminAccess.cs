namespace Origami.Core.Services;

public sealed class AdminAccess(
    AuthenticationStateProvider authenticationStateProvider,
    IDbContextFactory<CmsDbContext> contextFactory,
    IOptions<IdentityOptions> identityOptions)
{
    public async Task RequireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 絕不接受來自元件、請求內容或呼叫端的主體。
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (!await ValidatePrincipalAsync(state.User, contextFactory, identityOptions.Value, cancellationToken))
        {
            throw new UnauthorizedAccessException("Your administrator session has expired. Sign in again.");
        }
    }

    /// <summary>不擲回例外的版本，供公開頁面僅對管理員顯示私密內容。</summary>
    public async Task<bool> IsAdminAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        return await ValidatePrincipalAsync(state.User, contextFactory, identityOptions.Value, cancellationToken);
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

        // 電路不得保有 DbContext，也不得信任上次的週期性驗證。
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.AsNoTracking()
            .Where(user => user.Id == userId && user.SingletonKey == 1)
            .Select(user => new { user.SecurityStamp })
            .SingleOrDefaultAsync(cancellationToken);

        return user is not null && !string.IsNullOrEmpty(user.SecurityStamp)
            && string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal);
    }
}
