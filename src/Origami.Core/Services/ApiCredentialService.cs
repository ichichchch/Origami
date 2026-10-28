namespace Origami.Core.Services;

public sealed class ApiCredentialService(
    IDbContextFactory<CmsDbContext> factory,
    AdminAccess admin,
    TimeProvider timeProvider,
    IOptions<IdentityOptions>? identityOptions = null)
{
    private readonly IdentityOptions identity = identityOptions?.Value ?? new IdentityOptions();
    private const int SecretLength = 64;
    private const int TokenLength = 4 + 32 + 1 + SecretLength;

    public async Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken ct = default)
    {
        // 與建立／撤銷同一邊界：PAT 不得列舉任何憑證資訊，即使憑證屬於同一使用者。
        var user = await admin.RequireNonCredentialUserAsync(ct);
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow();
        // 保留全部有效憑證可撤銷，歷史只列最近紀錄，避免累積紀錄造成無界載入。
        return await db.ApiCredentials.AsNoTracking().Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.RevokedAt == null && x.ExpiresAt > now && x.SecurityStamp == user.SecurityStamp)
            .ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(100)
            .Select(x => new CredentialSummary(x.Id, x.Name, x.Scope, x.CreatedAt, x.ExpiresAt, x.RevokedAt)).ToListAsync(ct);
    }

    public async Task<CreatedCredential> CreateAsync(CredentialCreateInput input, CancellationToken ct = default)
    {
        var user = await admin.RequireNonCredentialUserAsync(ct);
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.Scope)) throw new CmsException("憑證範圍無效。");
        var name = (input.Name ?? "").Trim();
        if (name.Length is 0 or > Limits.CredentialName || name.Any(char.IsControl))
            throw new CmsException($"憑證名稱須為 1–{Limits.CredentialName} 字，且不得含控制字元。");
        var required = input.Scope == ApiCredentialScope.Read ? SiteRole.Reader : SiteRole.Editor;
        await admin.RequireRoleAsync(required, ct);
        ValidateExpiration(input.ExpiresAt, timeProvider.GetUtcNow());
        return await SiteAdministrationService.WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // 與成員／停用操作一致：先鎖站點，再鎖使用者，避免外鍵檢查造成相反鎖序。
            await SiteAdministrationService.LockSiteAsync(db, db.CurrentSiteId, ct);
            // 以全域使用者列鎖序列化建立，防止平行請求超過每人每站 20 張有效憑證。
            var currentUser = await db.Users
                .FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {user.Id} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException("登入已失效。");
            await admin.RequireNonCredentialUserAsync(ct);
            await admin.RequireRoleAsync(required, ct);
            var now = timeProvider.GetUtcNow();
            ValidateExpiration(input.ExpiresAt, now);
            if (currentUser.IsDisabled || string.IsNullOrEmpty(currentUser.SecurityStamp) ||
                !string.Equals(currentUser.SecurityStamp, user.SecurityStamp, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("登入已失效，請重新登入。");
            var count = await db.ApiCredentials.CountAsync(x => x.UserId == user.Id && x.RevokedAt == null &&
                x.ExpiresAt > now && x.SecurityStamp == currentUser.SecurityStamp, ct);
            if (count >= Limits.ApiCredentialMax)
                throw new CmsException($"每位使用者在每個站點最多只能有 {Limits.ApiCredentialMax} 張有效憑證。");

            // 固定格式：org_ + 32 位小寫 GUID + '.' + 64 位小寫十六進位秘密（32 隨機位元組）。
            var random = RandomNumberGenerator.GetBytes(32);
            string secret;
            try { secret = Convert.ToHexStringLower(random); }
            finally { CryptographicOperations.ZeroMemory(random); }
            var credential = new ApiCredential
            {
                SiteId = db.CurrentSiteId,
                UserId = user.Id,
                Name = name,
                SecretHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
                SecurityStamp = currentUser.SecurityStamp,
                Scope = input.Scope,
                CreatedAt = now,
                ExpiresAt = input.ExpiresAt.ToUniversalTime()
            };
            db.ApiCredentials.Add(credential);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            // 只有已提交成功的建立操作回傳秘密；列表與其他查詢永不載入原始秘密。
            return new CreatedCredential(credential.Id, $"org_{credential.Id:N}.{secret}", credential.ExpiresAt);
        });
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var user = await admin.RequireNonCredentialUserAsync(ct);
        await SiteAdministrationService.WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SiteAdministrationService.LockSiteAsync(db, db.CurrentSiteId, ct);
            var ownerId = await db.ApiCredentials.AsNoTracking().Where(x => x.Id == id)
                .Select(x => x.UserId).SingleOrDefaultAsync(ct) ?? throw new CmsException("憑證不存在。");
            if (ownerId != user.Id) await admin.RequireRoleAsync(SiteRole.Administrator, ct);
            _ = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {ownerId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw new CmsException("憑證不存在。");
            await admin.RequireNonCredentialUserAsync(ct);
            if (ownerId != user.Id) await admin.RequireRoleAsync(SiteRole.Administrator, ct);
            var credential = await db.ApiCredentials.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new CmsException("憑證不存在。");
            // 同一憑證重複撤銷保持原撤銷時間，不復活已撤銷的資料。
            if (credential.RevokedAt is null)
            {
                credential.RevokedAt = timeProvider.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return true;
        });
    }

    public async Task<ClaimsPrincipal?> AuthenticateAsync(string token, Guid siteId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (siteId == Guid.Empty || !TryParseToken(token, out var id, out var secret)) return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow();
        // 此方法供認證處理器使用，此時請求 scope 可能尚未設定站點；必須以顯式 siteId 隔離。
        var credential = await db.ApiCredentials.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == id && x.SiteId == siteId)
            .Select(x => new
            {
                x.UserId, x.SecretHash, x.SecurityStamp, x.Scope, x.ExpiresAt, x.RevokedAt,
                x.Site.IsActive, x.User.IsDisabled, x.User.SingletonKey,
                UserStamp = x.User.SecurityStamp, x.User.UserName
            }).SingleOrDefaultAsync(ct);
        if (credential is null || credential.RevokedAt is not null || credential.ExpiresAt <= now ||
            !credential.IsActive || credential.IsDisabled || !Enum.IsDefined(credential.Scope) ||
            string.IsNullOrEmpty(credential.UserStamp) ||
            !string.Equals(credential.SecurityStamp, credential.UserStamp, StringComparison.Ordinal)) return null;

        if (credential.SecretHash.Length != 64) return null;
        byte[] expected;
        try { expected = Convert.FromHexString(credential.SecretHash); }
        catch (FormatException) { return null; }
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        try
        {
            if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual)) return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);
        }
        if (credential.SingletonKey != 1 && !await db.SiteMemberships.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.SiteId == siteId && x.UserId == credential.UserId &&
                (x.Role == SiteRole.Reader || x.Role == SiteRole.Editor || x.Role == SiteRole.Administrator), ct)) return null;

        var claims = new List<Claim>
        {
            new(identity.ClaimsIdentity.UserIdClaimType, credential.UserId),
            new(identity.ClaimsIdentity.UserNameClaimType, credential.UserName ?? ""),
            new(identity.ClaimsIdentity.SecurityStampClaimType, credential.UserStamp),
            new(AccessClaims.CredentialId, id.ToString("N")),
            new(AccessClaims.CredentialScope, credential.Scope.ToString()),
            new(AccessClaims.SiteId, siteId.ToString("D"))
        };
        // 不加入 BFF 或管理員角色宣告，後續每次操作仍由 AdminAccess 查資料庫現行權限。
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Origami.ApiCredential",
            identity.ClaimsIdentity.UserNameClaimType, identity.ClaimsIdentity.RoleClaimType));
    }

    private static bool TryParseToken(string? token, out Guid id, out string secret)
    {
        id = Guid.Empty;
        secret = "";
        if (token is null || token.Length > 256 || token.Length != TokenLength ||
            !token.StartsWith("org_", StringComparison.Ordinal) || token[36] != '.' ||
            !Guid.TryParseExact(token.AsSpan(4, 32), "N", out id) || id == Guid.Empty)
            return false;
        // 固定小寫格式，拒絕空白、額外分隔字元及另一種秘密編碼。
        if (!token.AsSpan(4, 32).SequenceEqual(id.ToString("N"))) return false;
        secret = token[37..];
        return secret.Length == SecretLength && secret.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static void ValidateExpiration(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (expiresAt <= now || expiresAt > now.AddDays(Limits.ApiCredentialDays))
            throw new CmsException($"憑證到期時間必須晚於目前時間，且不超過 {Limits.ApiCredentialDays} 天。");
    }
}
