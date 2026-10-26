namespace Origami.Web.Services;

public sealed record TwoFactorStatus(bool Enabled, bool SetupPending);

/// <summary>Enables and disables TOTP two-factor sign-in for the single administrator.</summary>
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

    /// <summary>Generates (or regenerates) a pending secret and returns it with the otpauth URI.</summary>
    public async Task<(string Secret, string Uri)> BeginSetupAsync(CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var user = await SingleUserAsync(ct);
        user.TwoFactorSecret = Totp.GenerateSecret();
        user.TwoFactorEnabled = false;
        await UpdateAsync(user);
        return (user.TwoFactorSecret, Totp.OtpAuthUri(user.TwoFactorSecret, user.UserName ?? "admin", "Origami"));
    }

    /// <summary>Confirms a pending secret with a live code; enabling rotates the security stamp.</summary>
    public async Task EnableAsync(string code, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var user = await SingleUserAsync(ct);
        if (user.TwoFactorSecret == "") throw new CmsException("尚未產生設定金鑰，請先開始設定。");
        if (!Totp.Verify(user.TwoFactorSecret, code, clock)) throw new CmsException("驗證碼不正確，請確認時間同步後再試。");
        user.TwoFactorEnabled = true;
        await UpdateAsync(user);
        // Rotating the stamp keeps existing sign-ins honest about the new requirement.
        await users.UpdateSecurityStampAsync(user);
    }

    /// <summary>Disables two-factor after re-verifying the password and a live code.</summary>
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

    /// <summary>Loads the singleton administrator through the same context the UserManager tracks.</summary>
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
