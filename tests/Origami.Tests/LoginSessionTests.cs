namespace Origami.Tests;

/// <summary>
/// 登入工作階段：清單過濾、單一會話撤銷與 cookie 主體驗證的生效規則。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LoginSessionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Sessions_are_listed_newest_first_and_single_revocation_takes_effect()
    {
        var userId = await App.AdminUserIdAsync();
        var sessions = new LoginSessionService(App.ContextFactory);
        var now = TimeProvider.System.GetUtcNow();

        var first = NewSession(userId, now.AddMinutes(-10), now.AddHours(8));
        var second = NewSession(userId, now, now.AddHours(8));
        await using (var db = await App.CreateContextAsync())
        {
            db.LoginSessions.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        // 清單：新到舊、只含有效會話。
        var list = await sessions.ListAsync(userId);
        Assert.Equal(2, list.Count);
        Assert.Equal(second.Id, list[0].Id);

        // 撤銷單一會話：清單移除、被撤銷會話的主體驗證失效、另一個不受影響。
        await sessions.RevokeAsync(userId, first.Id);
        list = await sessions.ListAsync(userId);
        Assert.Single(list);
        Assert.False(await ValidateAsync(userId, first.Id));
        Assert.True(await ValidateAsync(userId, second.Id));

        // 撤銷不存在的會話視為錯誤。
        await Assert.ThrowsAsync<CmsException>(() => sessions.RevokeAsync(userId, Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task Missing_claim_expired_and_foreign_sessions_fail_principal_validation()
    {
        var userId = await App.AdminUserIdAsync();
        var clock = App.Services.GetRequiredService<TimeProvider>();
        await using var db = await App.CreateContextAsync();
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var principal = await CreatePrincipalAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(LoginSessionLifetime.CreateClaim(clock.GetUtcNow().AddHours(8)));

        // 缺少會話識別的主體不參與撤銷機制（戳記與期限仍把關）；帶識別才查撤銷資料列。
        Assert.True(await ValidateAsync(principal));

        // 已到期的會話資料列：拒絕。
        var expired = NewSession(userId, clock.GetUtcNow().AddHours(-9), clock.GetUtcNow().AddHours(-1));
        await db.LoginSessions.AddAsync(expired);
        await db.SaveChangesAsync();
        WithSession(principal, expired.Id);
        Assert.False(await ValidateAsync(principal));

        // 有效的會話資料列：接受。
        var live = NewSession(userId, clock.GetUtcNow().AddMinutes(-1), clock.GetUtcNow().AddHours(8));
        await db.LoginSessions.AddAsync(live);
        await db.SaveChangesAsync();
        WithSession(principal, live.Id);
        Assert.True(await ValidateAsync(principal));

        // 他人會話識別：拒絕，不可藉他人識別通過驗證。
        var other = NewSession("00000000-0000-0000-0000-000000000000", clock.GetUtcNow().AddMinutes(-1), clock.GetUtcNow().AddHours(8));
        await db.LoginSessions.AddAsync(other);
        await db.SaveChangesAsync();
        WithSession(principal, other.Id);
        Assert.False(await ValidateAsync(principal));
    }

    [PostgresFact]
    public async Task Cleanup_deletes_only_rows_past_retention()
    {
        var userId = await App.AdminUserIdAsync();
        var now = TimeProvider.System.GetUtcNow();
        var expiredOld = NewSession(userId, now.AddDays(-40), now.AddDays(-31));                 // 到期超過保留期：刪
        var revokedOld = NewSession(userId, now.AddDays(-40), now.AddHours(8));              // 撤銷超過保留期（未到期）：刪
        revokedOld.RevokedAt = now.AddDays(-35);
        var expiredRecent = NewSession(userId, now.AddDays(-29), now.AddDays(-28));              // 到期仍在保留期：留
        var revokedRecent = NewSession(userId, now.AddDays(-29), now.AddHours(8));               // 撤銷仍在保留期：留
        revokedRecent.RevokedAt = now.AddDays(-28);
        var active = NewSession(userId, now.AddMinutes(-1), now.AddHours(8));                    // 有效會話：留
        await using (var db = await App.CreateContextAsync())
        {
            db.LoginSessions.AddRange(expiredOld, revokedOld, expiredRecent, revokedRecent, active);
            await db.SaveChangesAsync();
        }

        var removed = await LoginSessionService.CleanupAsync(App.ContextFactory);

        Assert.Equal(2, removed);
        await using var verify = await App.CreateContextAsync();
        var remaining = await verify.LoginSessions.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync();
        Assert.Equal(3, remaining.Count);
        Assert.DoesNotContain(expiredOld.Id, remaining);
        Assert.DoesNotContain(revokedOld.Id, remaining);
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(AdminUser user)
    {
        using var scope = App.Services.CreateScope();
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AdminUser>>().CreateAsync(user);
        return principal;
    }

    private static LoginSessionRecord NewSession(string userId, DateTimeOffset created, DateTimeOffset expires) => new()
    {
        UserId = userId,
        CreatedAt = created,
        ExpiresAt = expires,
        UserAgent = "Mozilla/5.0 Origami Test",
        RemoteIp = "127.0.0.1",
        Provider = "password"
    };

    private static void WithSession(ClaimsPrincipal principal, Guid sessionId)
    {
        var identity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in identity.FindAll(AccessClaims.SessionId).ToArray()) identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(AccessClaims.SessionId, sessionId.ToString("N")));
    }

    private Task<bool> ValidateAsync(ClaimsPrincipal principal) =>
        AdminAccess.ValidatePrincipalAsync(principal, App.ContextFactory,
            App.Services.GetRequiredService<IOptions<IdentityOptions>>().Value, default,
            App.Services.GetRequiredService<TimeProvider>());

    private async Task<bool> ValidateAsync(string userId, Guid sessionId)
    {
        await using var db = await App.CreateContextAsync();
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var principal = await CreatePrincipalAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(
            LoginSessionLifetime.CreateClaim(TimeProvider.System.GetUtcNow().AddHours(8)));
        WithSession(principal, sessionId);
        return await ValidateAsync(principal);
    }
}
