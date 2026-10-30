namespace Origami.Core.Services;

public sealed class AdminAccess(
    AuthenticationStateProvider authenticationStateProvider,
    IDbContextFactory<CmsDbContext> contextFactory,
    IOptions<IdentityOptions> identityOptions,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task RequireAsync(CancellationToken cancellationToken = default) =>
        RequireRoleAsync(SiteRole.Editor, cancellationToken);

    public async Task RequireRoleAsync(SiteRole minimum, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(minimum)) throw new AccessDeniedException();
        var access = await GetCurrentAccessAsync(ct);
        if (!access.IsNetworkAdmin && (access.Role is null || access.Role.Value < minimum))
            throw new AccessDeniedException();
    }

    public async Task<AdminUser> RequireUserAsync(CancellationToken ct = default) =>
        (await GetValidatedUserAsync(ct)).User;

    public async Task<AdminUser> RequireNetworkAdminAsync(CancellationToken ct = default)
    {
        var session = await GetValidatedUserAsync(ct);
        if (session.CredentialScope is not null || session.User.SingletonKey != 1)
            throw new AccessDeniedException("此操作僅限全站管理者透過網站執行。");
        return session.User;
    }

    // 共用網站管理入口；不接受外部憑證，即使其擁有者是全站管理者。
    internal async Task<AdminUser> RequireNonCredentialUserAsync(CancellationToken ct)
    {
        var session = await GetValidatedUserAsync(ct);
        if (session.CredentialScope is not null)
            throw new AccessDeniedException("外部憑證不得管理帳號、成員或憑證，請使用網站。");
        return session.User;
    }

    public async Task<SiteAccessInfo> GetCurrentAccessAsync(CancellationToken ct = default)
    {
        var session = await GetValidatedUserAsync(ct);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        if (db.CurrentSiteId != session.SiteId ||
            !await db.Sites.AsNoTracking().AnyAsync(x => x.Id == session.SiteId && x.IsActive, ct))
            throw new AccessDeniedException("此站點不存在或已停用。");

        var networkAdmin = session.User.SingletonKey == 1;
        string displayName = "";
        SiteRole? role = null;
        if (networkAdmin)
        {
            role = SiteRole.Administrator;
        }
        else
        {
            var membership = await db.SiteMemberships.AsNoTracking()
                .Where(x => x.SiteId == session.SiteId && x.UserId == session.User.Id)
                .Select(x => new { x.Role, x.DisplayName }).SingleOrDefaultAsync(ct);
            if (membership is not null)
            {
                role = membership.Role;
                displayName = membership.DisplayName;
            }
        }
        if (role is not null && !Enum.IsDefined(role.Value)) role = null;

        if (session.CredentialScope is { } scope)
        {
            // 回傳有效權限而非原始職稱，避免 DTO 的 CanAdminister 將 PAT 提升為管理者。
            var maximum = scope == ApiCredentialScope.Read ? SiteRole.Reader : SiteRole.Editor;
            if (role is { } actual && actual > maximum) role = maximum;
            networkAdmin = false;
        }
        return new(session.User.Id, session.User.UserName ?? "", session.SiteId, networkAdmin, role, displayName);
    }

    /// <summary>只有當前站點的編輯者以上可以看私密內容；取消及資料庫故障不偽裝成未登入。</summary>
    public async Task<bool> IsAdminAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RequireRoleAsync(SiteRole.Editor, cancellationToken);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (AccessDeniedException) { return false; }
    }

    internal async Task<(AdminUser User, ApiCredentialScope? CredentialScope, Guid SiteId)> GetValidatedUserAsync(
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 每次從伺服器的驗證狀態重取主體，不信任元件或請求內容傳入的使用者與角色。
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var principal = state.User;
        if (!TryReadIdentity(principal, identityOptions.Value, out var userId, out var stamp))
            throw InvalidSession();

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || user.IsDisabled || string.IsNullOrEmpty(user.SecurityStamp) ||
            !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal))
            throw InvalidSession();

        // BFF 可以帶有 SiteId；只有憑證識別或範圍宣告才表示 PAT，殘缺宣告一律拒絕。
        if (!principal.HasClaim(x => x.Type is AccessClaims.CredentialId or AccessClaims.CredentialScope))
        {
            if (!LoginSessionLifetime.IsValid(principal, clock.GetUtcNow())) throw InvalidSession();
            return (user, null, db.CurrentSiteId);
        }

        if (!Guid.TryParse(SingleClaim(principal, AccessClaims.CredentialId), out var credentialId) ||
            !Guid.TryParse(SingleClaim(principal, AccessClaims.SiteId), out var siteId) ||
            siteId != db.CurrentSiteId || credentialId == Guid.Empty || siteId == Guid.Empty)
            throw InvalidSession();
        var scope = SingleClaim(principal, AccessClaims.CredentialScope) switch
        {
            "Read" or "0" => (ApiCredentialScope?)ApiCredentialScope.Read,
            "Write" or "1" => ApiCredentialScope.Write,
            _ => null
        };
        if (scope is null) throw InvalidSession();

        var now = clock.GetUtcNow();
        var credential = await db.ApiCredentials.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == credentialId && x.SiteId == siteId && x.UserId == user.Id)
            .Select(x => new { x.Scope, x.RevokedAt, x.ExpiresAt, x.SecurityStamp, x.Site.IsActive })
            .SingleOrDefaultAsync(ct);
        if (credential is null || credential.RevokedAt is not null || credential.ExpiresAt <= now ||
            !credential.IsActive || credential.Scope != scope ||
            !string.Equals(credential.SecurityStamp, user.SecurityStamp, StringComparison.Ordinal))
            throw InvalidSession();

        if (user.SingletonKey != 1 && !await db.SiteMemberships.AsNoTracking()
            .AnyAsync(x => x.SiteId == siteId && x.UserId == user.Id &&
                (x.Role == SiteRole.Reader || x.Role == SiteRole.Author || x.Role == SiteRole.Editor || x.Role == SiteRole.Administrator), ct))
            throw InvalidSession();
        return (user, scope, siteId);
    }

    /// <summary>Cookie 驗證只確認全域帳號，不要求使用者已加入目前站點。</summary>
    public static async Task<bool> ValidatePrincipalAsync(
        ClaimsPrincipal? principal,
        IDbContextFactory<CmsDbContext> contextFactory,
        IdentityOptions identityOptions,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = timeProvider ?? TimeProvider.System;
        if (!TryReadIdentity(principal, identityOptions, out var userId, out var stamp) ||
            principal!.HasClaim(x => x.Type is AccessClaims.CredentialId or AccessClaims.CredentialScope) ||
            !LoginSessionLifetime.IsValid(principal, clock.GetUtcNow())) return false;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.IsDisabled, x.SecurityStamp }).SingleOrDefaultAsync(cancellationToken);
        return user is not null && !user.IsDisabled && !string.IsNullOrEmpty(user.SecurityStamp) &&
            string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal) &&
            LoginSessionLifetime.IsValid(principal, clock.GetUtcNow());
    }

    private static bool TryReadIdentity(ClaimsPrincipal? principal, IdentityOptions options,
        out string? userId, out string? stamp)
    {
        userId = principal is null ? null : SingleClaim(principal, options.ClaimsIdentity.UserIdClaimType);
        stamp = principal is null ? null : SingleClaim(principal, options.ClaimsIdentity.SecurityStampClaimType);
        return principal?.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(stamp);
    }

    private static string? SingleClaim(ClaimsPrincipal principal, string type)
    {
        var claims = principal.FindAll(type).Take(2).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }

    private static UnauthorizedAccessException InvalidSession() =>
        new("登入或憑證已失效，請重新登入。");
}
