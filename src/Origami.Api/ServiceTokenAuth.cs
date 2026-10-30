namespace Origami.Api;

public static class ServiceTokenAuth
{
    public const string SchemeName = "OrigamiServiceToken";
    public const string BffOnlyPolicy = "BffOnly";

    public static bool IsBff(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true && principal.HasClaim(AccessClaims.Bff, "true");
}

public sealed class ServiceTokenOptions : AuthenticationSchemeOptions
{
    /// <summary>僅供 BFF 使用的共用金鑰；未設定時仍可驗證已發行的外部憑證。</summary>
    public string? Token { get; set; }
}

/// <summary>只從 Authorization 標頭驗證 BFF 共用金鑰或綁定站點的外部憑證。</summary>
public sealed class ServiceTokenHandler(
    IOptionsMonitor<ServiceTokenOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    SiteContext siteContext, ApiCredentialService credentials)
    : AuthenticationHandler<ServiceTokenOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers.Authorization;
        if (headers.Count == 0) return AuthenticateResult.NoResult();
        if (headers.Count != 1) return AuthenticateResult.Fail("Invalid authorization header.");
        var header = headers[0] ?? "";
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        var presented = header[prefix.Length..].Trim();

        // Options（依 scheme 命名取得）才是已設定的執行個體；監視器的 CurrentValue 並不是。
        var expected = Options.Token;
        if (!string.IsNullOrWhiteSpace(expected) && MatchesServiceToken(presented, expected))
        {
            // BFF 是主機，不是使用者；不得在此合成管理員 id、安全戳記或角色。
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "origami-bff"),
                new Claim(AccessClaims.Bff, "true"),
            ], Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }

        if (presented.StartsWith("org_", StringComparison.Ordinal))
        {
            var principal = await credentials.AuthenticateAsync(presented, siteContext.SiteId, Context.RequestAborted);
            if (principal is not null)
                return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        // 不揭露憑證是否存在、過期、撤銷或屬於其他站點，也不記錄傳入的秘密。
        return AuthenticateResult.Fail("Invalid bearer token.");
    }

    private static bool MatchesServiceToken(string presented, string expected)
    {
        if (presented.Length != expected.Length) return false;
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        try
        {
            return presentedBytes.Length == expectedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(presentedBytes);
            CryptographicOperations.ZeroMemory(expectedBytes);
        }
    }
}

/// <summary>
/// PAT 沿用處理器驗過的主體；只有可信 BFF 能轉發使用者 id 與工作階段安全戳記。
/// 此處不查詢或合成資料庫的管理員；現行帳號、站點角色與戳記由 Core 每次重驗。
/// </summary>
public sealed class AdminPrincipalProvider(
    IHttpContextAccessor accessor,
    IOptions<IdentityOptions> identityOptions) : AuthenticationStateProvider
{
    public const string StampHeader = AccessClaims.StampHeader;
    public const string UserHeader = AccessClaims.UserHeader;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var context = accessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true) return Anonymous();
        if (!ServiceTokenAuth.IsBff(context.User))
        {
            // 外部憑證不能以轉發標頭覆寫已驗證的使用者、站點或範圍。
            return context.User.HasClaim(claim => claim.Type == AccessClaims.CredentialId)
                ? Task.FromResult(new AuthenticationState(context.User))
                : Anonymous();
        }

        var users = context.Request.Headers[UserHeader];
        var stamps = context.Request.Headers[StampHeader];
        var deadlines = context.Request.Headers[AccessClaims.SessionExpiresHeader];
        // 同時拒絕多個標頭值及代理折疊後的逗號清單，不能任意挑一個身分。
        if (users.Count > 1 || stamps.Count > 1 || deadlines.Count > 1 ||
            (users.Count == 1 && users[0]?.Contains(',') == true) ||
            (stamps.Count == 1 && stamps[0]?.Contains(',') == true) ||
            (deadlines.Count == 1 && deadlines[0]?.Contains(',') == true))
            throw new BadHttpRequestException("轉發身分標頭不可重複。");
        var userId = users.Count == 1 ? users[0] : null;
        var stamp = stamps.Count == 1 ? stamps[0] : null;
        // BFF 也代表匿名訪客取公開呈現資料；沒有使用者時不建立工作階段。
        if (string.IsNullOrWhiteSpace(userId)) return Anonymous();
        if (string.IsNullOrWhiteSpace(stamp) || deadlines.Count != 1)
            throw new UnauthorizedAccessException("登入已失效，請重新登入。");
        var types = identityOptions.Value.ClaimsIdentity;
        var identity = new ClaimsIdentity(
        [
            new Claim(types.UserIdClaimType, userId),
            new Claim(types.SecurityStampClaimType, stamp),
            new Claim(AccessClaims.SessionExpiresAt, deadlines[0] ?? ""),
        ], ServiceTokenAuth.SchemeName, types.UserNameClaimType, types.RoleClaimType);
        var principal = new ClaimsPrincipal(identity);
        if (!LoginSessionLifetime.TryRead(principal, out _))
            throw new BadHttpRequestException("轉發工作階段期限無效。");
        // 截止時間與現行安全戳記由 Core 在每次操作重驗，PAT 永不走此分支。
        return Task.FromResult(new AuthenticationState(principal));
    }

    private static Task<AuthenticationState> Anonymous() =>
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
}
