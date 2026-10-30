namespace Origami.Web.Services;

public sealed class WebSignInFlow(
    UserManager<AdminUser> users,
    SignInManager<AdminUser> signIn,
    IDbContextFactory<CmsDbContext> factory,
    IOptions<IdentityOptions> identityOptions,
    IDataProtectionProvider protection,
    SiteContext site,
    TimeProvider clock,
    LoginThrottle throttle)
{
    public const string PendingCookie = "origami.2fa";
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(5);
    private readonly IDataProtector pendingProtector = protection.CreateProtector("Origami.TwoFactorPending,v2");

    public static string Origin(HttpContext context) =>
        ExternalProviderCatalog.NormalizeOrigin($"{context.Request.Scheme}://{context.Request.Host.ToUriComponent()}") ?? "";

    public bool TryAcquire(HttpContext context) => throttle.TryAcquire(context.Connection.RemoteIpAddress?.ToString());

    public async Task<AdminUser?> CheckPasswordAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 256 || string.IsNullOrEmpty(password) || password.Length > 1024)
            return null;
        var user = await users.FindByNameAsync(username.Trim());
        return user is not null && await VerifyPasswordAsync(user, password) ? user : null;
    }

    public async Task<bool> BeginAsync(HttpContext context, AdminUser user, bool remember,
        string? provider = null, string? providerKey = null)
    {
        ClearPending(context);
        if (!await CanSignInAsync(user)) throw new CmsException("無法登入，請重新驗證帳號。");
        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrEmpty(user.TwoFactorSecret)) throw new CmsException("無法完成兩步驟驗證，請聯絡管理者。");
            var pending = new PendingSignIn
            {
                UserId = user.Id, SecurityStamp = user.SecurityStamp ?? "", Remember = remember,
                Origin = Origin(context), SiteId = site.SiteId, ExpiresAt = clock.GetUtcNow().Add(PendingLifetime),
                Provider = provider, ProviderKey = providerKey
            };
            context.Response.Cookies.Append(PendingCookie, pendingProtector.Protect(JsonSerializer.Serialize(pending)),
                new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = PendingLifetime });
            return false;
        }
        await SignInAsync(context, user, remember);
        return true;
    }

    public PendingSignIn? ReadPending(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(PendingCookie, out var value) || string.IsNullOrEmpty(value)) return null;
        try
        {
            var pending = JsonSerializer.Deserialize<PendingSignIn>(pendingProtector.Unprotect(value));
            if (pending is null || pending.ExpiresAt <= clock.GetUtcNow() || pending.SiteId != site.SiteId ||
                !string.Equals(pending.Origin, Origin(context), StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(pending.UserId) || string.IsNullOrEmpty(pending.SecurityStamp) ||
                (pending.Provider is null) != (pending.ProviderKey is null)) return null;
            return pending;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    public async Task<bool> CompleteSecondFactorAsync(HttpContext context, string code)
    {
        var pending = ReadPending(context);
        ClearPending(context);
        if (pending is null) return false;
        var user = await users.FindByIdAsync(pending.UserId);
        if (user is null || !await CanSignInAsync(user) || !user.TwoFactorEnabled ||
            string.IsNullOrEmpty(user.TwoFactorSecret) ||
            !string.Equals(user.SecurityStamp, pending.SecurityStamp, StringComparison.Ordinal)) return false;
        if (pending.Provider is not null)
        {
            var linkedUser = await users.FindByLoginAsync(pending.Provider, pending.ProviderKey!);
            if (linkedUser?.Id != user.Id) return false;
        }
        if (!await VerifySecondFactorAsync(user, code)) return false;
        await SignInAsync(context, user, pending.Remember);
        return true;
    }

    public async Task<AdminUser?> ReauthenticateAsync(HttpContext context, string password, string code)
    {
        if (!await AdminAccess.ValidatePrincipalAsync(context.User, factory, identityOptions.Value,
                context.RequestAborted, clock)) return null;
        var user = await users.GetUserAsync(context.User);
        if (user is null || string.IsNullOrEmpty(password) || password.Length > 1024 ||
            !await VerifyPasswordAsync(user, password)) return null;
        if (user.TwoFactorEnabled && !await VerifySecondFactorAsync(user, code)) return null;
        EnsureSucceeded(await users.ResetAccessFailedCountAsync(user));
        return user;
    }

    public static void ClearPending(HttpContext context) => context.Response.Cookies.Delete(PendingCookie,
        new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax });

    private async Task<bool> CanSignInAsync(AdminUser user) => !user.IsDisabled &&
        !string.IsNullOrEmpty(user.SecurityStamp) && await signIn.CanSignInAsync(user) && !await users.IsLockedOutAsync(user);

    private async Task<bool> VerifyPasswordAsync(AdminUser user, string password)
    {
        if (!await CanSignInAsync(user)) return false;
        // 框架不認得自訂 TOTP，不能讓密碼成功提前重設兩步驗證的失敗次數。
        if (await users.CheckPasswordAsync(user, password)) return true;
        EnsureSucceeded(await users.AccessFailedAsync(user));
        return false;
    }

    private async Task<bool> VerifySecondFactorAsync(AdminUser user, string code)
    {
        if (string.IsNullOrEmpty(user.TwoFactorSecret) || string.IsNullOrWhiteSpace(code) || code.Length > 10) return false;
        var totp = Totp.Verify(user.TwoFactorSecret, code, clock);
        var recovery = !totp && TwoFactorService.TryRedeemRecoveryCode(user, code);
        if (!totp && !recovery)
        {
            EnsureSucceeded(await users.AccessFailedAsync(user));
            return false;
        }
        if (recovery) EnsureSucceeded(await users.UpdateAsync(user));
        EnsureSucceeded(await users.ResetAccessFailedCountAsync(user));
        return true;
    }

    private async Task SignInAsync(HttpContext context, AdminUser user, bool remember)
    {
        EnsureSucceeded(await users.ResetAccessFailedCountAsync(user));
        var issued = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        var expires = issued.Add(remember ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8));
        var properties = new AuthenticationProperties
        {
            IsPersistent = remember, AllowRefresh = false, IssuedUtc = issued, ExpiresUtc = expires
        };
        await signIn.SignInWithClaimsAsync(user, properties, [LoginSessionLifetime.CreateClaim(expires)]);
        ClearPending(context);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded) throw new CmsException("驗證狀態已變更，請重新登入再試。");
    }

    public sealed class PendingSignIn
    {
        public string UserId { get; init; } = "";
        public string SecurityStamp { get; init; } = "";
        public bool Remember { get; init; }
        public string Origin { get; init; } = "";
        public Guid SiteId { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public string? Provider { get; init; }
        public string? ProviderKey { get; init; }
    }
}
