namespace Origami.Web.Services;

// 僅有 CLI 命令與測試會呼叫此服務；刻意不提供 HTTP 端點。
public sealed class AdminProvisioner(IServiceScopeFactory scopeFactory)
{
    public const int MaximumUserNameLength = 256;
    public const int MaximumPasswordLength = 1024;

    public async Task InitializeAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length > MaximumUserNameLength)
        {
            throw new AdminProvisioningException("請輸入 1 至 256 字元的帳號。");
        }
        ValidatePasswordLength(password);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // UserManager 的 store／context 只存活於本次操作，絕不進入電路。
            await using var scope = scopeFactory.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            if (await manager.Users.AnyAsync(cancellationToken))
            {
                throw new AdminProvisioningException("管理員已存在。請使用 admin-reset 恢復存取權。");
            }

            var user = new AdminUser { UserName = username.Trim(), SingletonKey = 1, LockoutEnabled = true };
            cancellationToken.ThrowIfCancellationRequested();
            // 資料庫對 SingletonKey 的唯一約束，同時防護並行競爭的初始化。
            EnsureSucceeded(await manager.CreateAsync(user, password));
        }
        catch (DbUpdateException)
        {
            throw new AdminProvisioningException(
                "無法建立管理員。可能已存在管理員，請確認資料庫可用且已套用遷移。");
        }
        catch (DbException)
        {
            throw DatabaseUnavailable();
        }
    }

    public async Task ResetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        ValidatePasswordLength(password);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = await manager.Users.SingleOrDefaultAsync(cancellationToken);
            if (user is null)
            {
                throw new AdminProvisioningException("尚未存在管理員。請先執行 admin-init。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var token = await manager.GeneratePasswordResetTokenAsync(user);
            // 復原時也一併清除鎖定，與密碼／安全戳記在同一筆儲存完成。
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            // 驗證器遺失時由伺服器端復原：重設也會停用兩步驗證。
            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = "";
            cancellationToken.ThrowIfCancellationRequested();
            // Identity 重設密碼雜湊時，會一併以原子方式重設安全戳記。
            EnsureSucceeded(await manager.ResetPasswordAsync(user, token, password));
        }
        catch (DbUpdateException)
        {
            throw new AdminProvisioningException("無法重設密碼。請重新載入管理員後再試一次。");
        }
        catch (DbException)
        {
            throw DatabaseUnavailable();
        }
    }

    private static void ValidatePasswordLength(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > MaximumPasswordLength)
        {
            throw new AdminProvisioningException("請輸入符合密碼規則且長度不超過 1024 字的密碼。");
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        if (result.Errors.Any(error => error.Code.StartsWith("Password", StringComparison.Ordinal)))
        {
            throw new AdminProvisioningException(
                "密碼至少 8 字，需含大寫、小寫、數字、符號，且至少有四個不同字元。");
        }
        if (result.Errors.Any(error => error.Code == "ConcurrencyFailure"))
        {
            throw new AdminProvisioningException("操作期間管理員已變更，請再試一次。");
        }
        if (result.Errors.Any(error => error.Code == "DuplicateUserName"))
        {
            throw new AdminProvisioningException("管理員已存在。請使用 admin-reset 恢復存取權。");
        }
        if (result.Errors.Any(error => error.Code == "InvalidUserName"))
        {
            throw new AdminProvisioningException("帳號僅可使用英數字或 -._@+，請更換後再試。");
        }
        throw new AdminProvisioningException("無法更新管理員。請確認輸入後再試一次。");
    }

    private static AdminProvisioningException DatabaseUnavailable() =>
        new("管理員資料庫無法使用。請確認設定並套用遷移後再試。");
}

// 其訊息刻意設計為可安全顯示；不暴露底層資料庫錯誤。
public sealed class AdminProvisioningException(string message) : InvalidOperationException(message);
