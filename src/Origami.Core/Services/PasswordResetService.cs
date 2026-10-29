namespace Origami.Core.Services;

/// <summary>
/// 密碼重設流程：以寄件方式提供重設連結，並以 Identity 權杖完成重設。
/// 申請一律回傳相同結果，避免被用來列舉已註冊的電子郵件。
/// </summary>
public sealed class PasswordResetService(UserManager<AdminUser> users, IEmailSender mail)
{
    /// <summary>重設權杖有效期（分鐘）。</summary>
    public const int TokenMinutes = 30;

    public sealed record ResetOutcome(bool Succeeded, string Message);

    /// <summary>
    /// 申請重設連結。帳號存在、啟用且郵件服務可用時寄出連結；其餘情況安靜略過。
    /// 回傳值固定為 true，呼叫端對外顯示一致訊息。
    /// </summary>
    public async Task<bool> RequestResetAsync(string email, string origin, CancellationToken ct = default)
    {
        var address = (email ?? "").Trim();
        if (address.Length == 0 || address.Length > 256) return true;
        var user = await users.FindByEmailAsync(address);
        if (user is null || user.IsDisabled || !mail.IsConfigured) return true;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = $"{origin.TrimEnd('/')}/account/reset-password?user={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
        var body = "你收到這封信是因為有人要求重設 Origami 帳號的密碼。\n\n" +
            $"請在 {TokenMinutes} 分鐘內開啟以下連結完成重設：\n{link}\n\n" +
            "若非本人操作，請忽略此信，密碼不會變更。";
        await mail.SendAsync(address, "重設你的 Origami 密碼", body, ct);
        return true;
    }

    /// <summary>以權杖完成重設；權杖過期、帳號不符或密碼不符規則都回傳明確但不含內部細節的結果。</summary>
    public async Task<ResetOutcome> ResetAsync(string userId, string token, string email, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token)) 
            return new(false, "重設連結無效或已過期，請重新申請。");
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length > 1024)
            return new(false, "新密碼不符合規則，請重新申請。");
        var user = await users.FindByIdAsync(userId);
        if (user is null || user.IsDisabled ||
            !string.Equals(user.Email, (email ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return new(false, "重設連結無效或已過期，請重新申請。");
        var result = await users.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            return new(false, "重設連結無效或已過期，或新密碼未符合規則，請重新申請。");
        // 重設後輪替安全戳記，既有登入與工作階段立即失效。
        await users.UpdateSecurityStampAsync(user);
        return new(true, "密碼已更新。");
    }
}
