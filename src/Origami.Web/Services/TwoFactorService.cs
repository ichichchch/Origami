namespace Origami.Web.Services;

public sealed record TwoFactorStatus(bool Enabled, bool SetupPending, int RecoveryCodesLeft = 0);

/// <summary>啟用與停用目前已登入使用者自己的 TOTP 兩步驗證，以及管理一次性備用恢復碼。</summary>
public sealed class TwoFactorService(
    AdminAccess admin,
    UserManager<AdminUser> users,
    SignInManager<AdminUser> signIn,
    TimeProvider clock)
{
    public async Task<TwoFactorStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var user = await CurrentUserAsync(ct);
        var count = string.IsNullOrWhiteSpace(user.RecoveryCodeHashes)
            ? 0
            : user.RecoveryCodeHashes.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
        return new(user.TwoFactorEnabled, !user.TwoFactorEnabled && user.TwoFactorSecret != "", count);
    }

    /// <summary>產生（或重新產生）待確認的密鑰，連同 otpauth URI 一併回傳。</summary>
    public async Task<(string Secret, string Uri)> BeginSetupAsync(CancellationToken ct = default)
    {
        var user = await CurrentUserAsync(ct);
        user.TwoFactorSecret = Totp.GenerateSecret();
        user.TwoFactorEnabled = false;
        user.RecoveryCodeHashes = "";
        await UpdateAsync(user);
        return (user.TwoFactorSecret, Totp.OtpAuthUri(user.TwoFactorSecret, user.UserName ?? "admin", "Origami"));
    }

    /// <summary>以有效驗證碼確認待確認密鑰；啟用時會輪替安全戳記，並回傳一次性備用恢復碼明文清單。</summary>
    public async Task<IReadOnlyList<string>> EnableAsync(string code, CancellationToken ct = default)
    {
        var user = await CurrentUserAsync(ct);
        if (user.TwoFactorSecret == "") throw new CmsException("尚未產生設定金鑰，請先開始設定。");
        if (!Totp.Verify(user.TwoFactorSecret, code, clock)) throw new CmsException("驗證碼不正確，請確認時間同步後再試。");
        var (plainCodes, hashJoined) = GenerateRecoveryCodes();
        user.TwoFactorEnabled = true;
        user.RecoveryCodeHashes = hashJoined;
        await UpdateAsync(user);
        // 輪替安全戳記，迫使既有登入遵守新加入的驗證要求。
        await users.UpdateSecurityStampAsync(user);
        return plainCodes;
    }

    /// <summary>重新產生一次性備用恢復碼（舊恢復碼立即失效）。</summary>
    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(CancellationToken ct = default)
    {
        var user = await CurrentUserAsync(ct);
        if (!user.TwoFactorEnabled) throw new CmsException("請先啟用兩步驟驗證。");
        var (plainCodes, hashJoined) = GenerateRecoveryCodes();
        user.RecoveryCodeHashes = hashJoined;
        await UpdateAsync(user);
        return plainCodes;
    }

    /// <summary>重新驗證密碼與有效驗證碼（或備用恢復碼）後，才停用兩步驗證。</summary>
    public async Task DisableAsync(string password, string code, CancellationToken ct = default)
    {
        var user = await CurrentUserAsync(ct);
        var passwordOk = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        var codeOk = Totp.Verify(user.TwoFactorSecret, code, clock) || TryRedeemRecoveryCode(user, code);
        if (!passwordOk.Succeeded || !codeOk)
            throw new CmsException("密碼或驗證碼不正確，無法停用兩步驟驗證。");
        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = "";
        user.RecoveryCodeHashes = "";
        await UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user);
    }

    /// <summary>登入時驗證並消耗一組一次性備用恢復碼；成功時會自使用者紀錄移除該雜湊（須儲存）。</summary>
    public static bool TryRedeemRecoveryCode(AdminUser user, string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode) || string.IsNullOrWhiteSpace(user.RecoveryCodeHashes))
            return false;
        var normalized = rawCode.Trim().Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Length != 8) return false;
        var candidateHash = HashRecoveryCode(normalized);
        var existing = user.RecoveryCodeHashes.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var matchedIndex = -1;
        for (var i = 0; i < existing.Count; i++)
        {
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(existing[i]),
                Encoding.ASCII.GetBytes(candidateHash)))
            {
                matchedIndex = i;
                break;
            }
        }
        if (matchedIndex < 0) return false;
        existing.RemoveAt(matchedIndex);
        user.RecoveryCodeHashes = string.Join(';', existing);
        return true;
    }

    public static (IReadOnlyList<string> PlainCodes, string HashJoined) GenerateRecoveryCodes()
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var plain = new List<string>(Limits.RecoveryCodesCount);
        var hashes = new List<string>(Limits.RecoveryCodesCount);
        Span<byte> buf = stackalloc byte[8];
        for (var i = 0; i < Limits.RecoveryCodesCount; i++)
        {
            RandomNumberGenerator.Fill(buf);
            var chars = new char[8];
            for (var j = 0; j < 8; j++)
                chars[j] = alphabet[buf[j] % alphabet.Length];
            var raw = new string(chars);
            plain.Add(raw[..4] + "-" + raw[4..]);
            hashes.Add(HashRecoveryCode(raw));
        }
        return (plain, string.Join(';', hashes));
    }

    private static string HashRecoveryCode(string normalizedEightChars) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("origami-recovery:" + normalizedEightChars)));

    /// <summary>先重驗全球身分，再由 UserManager 的同一 context 載入本人，絕不挑選 bootstrap。</summary>
    private async Task<AdminUser> CurrentUserAsync(CancellationToken ct)
    {
        var currentUser = await admin.RequireUserAsync(ct);
        var user = await users.Users.SingleOrDefaultAsync(x => x.Id == currentUser.Id, ct);
        if (user is null || user.IsDisabled) throw new UnauthorizedAccessException("登入已失效，請重新登入。");
        return user;
    }

    private async Task UpdateAsync(AdminUser user)
    {
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) throw new CmsException("無法儲存兩步驟驗證設定，請再試一次。");
    }
}
