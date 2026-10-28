namespace Origami.Web.Services;

public sealed record TwoFactorStatus(bool Enabled, bool SetupPending);

/// <summary>啟用與停用目前已登入使用者自己的 TOTP 兩步驗證。</summary>
public sealed class TwoFactorService(
    AdminAccess admin,
    UserManager<AdminUser> users,
    SignInManager<AdminUser> signIn,
    TimeProvider clock)
{
    public async Task<TwoFactorStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var user = await CurrentUserAsync(ct);
        return new(user.TwoFactorEnabled, !user.TwoFactorEnabled && user.TwoFactorSecret != "");
    }

    /// <summary>產生（或重新產生）待確認的密鑰，連同 otpauth URI 一併回傳。</summary>
    public async Task<(string Secret, string Uri)> BeginSetupAsync(CancellationToken ct = default)
    {
        var user = await CurrentUserAsync(ct);
        user.TwoFactorSecret = Totp.GenerateSecret();
        user.TwoFactorEnabled = false;
        await UpdateAsync(user);
        return (user.TwoFactorSecret, Totp.OtpAuthUri(user.TwoFactorSecret, user.UserName ?? "admin", "Origami"));
    }

    /// <summary>以有效驗證碼確認待確認密鑰；啟用時會輪替安全戳記。</summary>
    public async Task EnableAsync(string code, CancellationToken ct = default)
    {
        var user = await CurrentUserAsync(ct);
        if (user.TwoFactorSecret == "") throw new CmsException("尚未產生設定金鑰，請先開始設定。");
        if (!Totp.Verify(user.TwoFactorSecret, code, clock)) throw new CmsException("驗證碼不正確，請確認時間同步後再試。");
        user.TwoFactorEnabled = true;
        await UpdateAsync(user);
        // 輪替安全戳記，迫使既有登入遵守新加入的驗證要求。
        await users.UpdateSecurityStampAsync(user);
    }

    /// <summary>重新驗證密碼與有效驗證碼後，才停用兩步驗證。</summary>
    public async Task DisableAsync(string password, string code, CancellationToken ct = default)
    {
        var user = await CurrentUserAsync(ct);
        var passwordOk = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        if (!passwordOk.Succeeded || !Totp.Verify(user.TwoFactorSecret, code, clock))
            throw new CmsException("密碼或驗證碼不正確，無法停用兩步驟驗證。");
        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = "";
        await UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user);
    }

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
