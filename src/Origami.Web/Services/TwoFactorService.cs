namespace Origami.Web.Services;

public sealed record TwoFactorStatus(bool Enabled, bool SetupPending);

/// <summary>啟用與停用單一管理員的 TOTP 兩步驗證登入。</summary>
public sealed class TwoFactorService(
    AdminAccess admin,
    UserManager<AdminUser> users,
    SignInManager<AdminUser> signIn,
    TimeProvider clock)
{
    public async Task<TwoFactorStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var user = await SingleUserAsync(ct);
        return new(user.TwoFactorEnabled, !user.TwoFactorEnabled && user.TwoFactorSecret != "");
    }

    /// <summary>產生（或重新產生）待確認的密鑰，連同 otpauth URI 一併回傳。</summary>
    public async Task<(string Secret, string Uri)> BeginSetupAsync(CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var user = await SingleUserAsync(ct);
        user.TwoFactorSecret = Totp.GenerateSecret();
        user.TwoFactorEnabled = false;
        await UpdateAsync(user);
        return (user.TwoFactorSecret, Totp.OtpAuthUri(user.TwoFactorSecret, user.UserName ?? "admin", "Origami"));
    }

    /// <summary>以有效驗證碼確認待確認密鑰；啟用時會輪替安全戳記。</summary>
    public async Task EnableAsync(string code, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var user = await SingleUserAsync(ct);
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
        await admin.RequireAsync(ct);
        var user = await SingleUserAsync(ct);
        var passwordOk = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        if (!passwordOk.Succeeded || !Totp.Verify(user.TwoFactorSecret, code, clock))
            throw new CmsException("密碼或驗證碼不正確，無法停用兩步驟驗證。");
        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = "";
        await UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user);
    }

    /// <summary>經由 UserManager 追蹤的同一個 context 載入單例管理員。</summary>
    private async Task<AdminUser> SingleUserAsync(CancellationToken ct)
    {
        var user = await users.Users.SingleOrDefaultAsync(x => x.SingletonKey == 1, ct);
        return user ?? throw new CmsException("找不到管理員帳號。");
    }

    private async Task UpdateAsync(AdminUser user)
    {
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) throw new CmsException("無法儲存兩步驟驗證設定，請再試一次。");
    }
}
