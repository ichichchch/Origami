namespace Origami.Core.Services;

/// <summary>網站登入的固定截止時間；不以活動或主體刷新延長，PAT 另依資料庫期限驗證。</summary>
public static class LoginSessionLifetime
{
    public static bool TryRead(ClaimsPrincipal? principal, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (principal is null) return false;
        var claims = principal.FindAll(AccessClaims.SessionExpiresAt).Take(2).ToArray();
        if (claims.Length != 1) return false;
        var value = claims[0].Value;
        // 僅接受標準十進位 Unix 秒，不接受空白、正號、小數、前導零或文化相關格式。
        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds) ||
            !string.Equals(value, seconds.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return false;
        try
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public static bool IsValid(ClaimsPrincipal? principal, DateTimeOffset now) =>
        TryRead(principal, out var expiresAt) && expiresAt > now;

    public static Claim CreateClaim(DateTimeOffset expiresAt) =>
        new(AccessClaims.SessionExpiresAt, expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer64);
}
