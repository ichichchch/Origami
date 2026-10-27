namespace Origami.Api;

public static class ServiceTokenAuth
{
    public const string SchemeName = "OrigamiServiceToken";
}

public sealed class ServiceTokenOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// 來自外部設定（Api:ServiceToken）的共用金鑰。未設定時，
    /// 所有管理端點保持關閉並回應 401，因此寫入介面不可能
    /// 被意外啟用。
    /// </summary>
    public string? Token { get; set; }
}

/// <summary>
/// BFF 憑證：由網站主機持有的單一共用金鑰。瀏覽器接觸不到任何 token；
/// 管理員身份先在網站內確認（cookie＋兩步驗證）後，才會呼叫這裡。
/// </summary>
public sealed class ServiceTokenHandler(
    IOptionsMonitor<ServiceTokenOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<ServiceTokenOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Options（依 scheme 命名取得）才是已設定的執行個體；監視器的 CurrentValue 並不是。
        var expected = Options.Token;
        if (string.IsNullOrWhiteSpace(expected))
            return Task.FromResult(AuthenticateResult.NoResult());
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());
        var presented = header[prefix.Length..].Trim();
        var valid = presented.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
        if (!valid)
            return Task.FromResult(AuthenticateResult.Fail("Invalid service token."));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "origami-bff"),
        ], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

/// <summary>
/// 提供現行的管理員主體，讓 AdminAccess 在每次操作時重新向資料庫驗證使用者 id 與
/// 安全戳記，與網站主機內的做法完全一致：密碼重設（安全戳記輪替）
/// 也會立即撤銷此處的寫入權限。
/// </summary>
public sealed class AdminPrincipalProvider(
    IDbContextFactory<CmsDbContext> factory,
    IOptions<IdentityOptions> identityOptions) : AuthenticationStateProvider
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
        var admin = await db.Users.AsNoTracking()
            .Where(user => user.SingletonKey == 1)
            .Select(user => new { user.Id, user.SecurityStamp })
            .SingleOrDefaultAsync(CancellationToken.None);
        if (admin is null || string.IsNullOrEmpty(admin.SecurityStamp))
            return new(new ClaimsPrincipal(new ClaimsIdentity()));
        var claimTypes = identityOptions.Value.ClaimsIdentity;
        var identity = new ClaimsIdentity(
        [
            new Claim(claimTypes.UserIdClaimType, admin.Id),
            new Claim(claimTypes.SecurityStampClaimType, admin.SecurityStamp),
        ], ServiceTokenAuth.SchemeName);
        return new(new ClaimsPrincipal(identity));
    }
}
