namespace Origami.Web.Services;

// Only CLI commands and tests call this service. There is deliberately no HTTP endpoint.
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
            // UserManager's store/context lives only for this operation, never in a circuit.
            await using var scope = scopeFactory.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            if (await manager.Users.AnyAsync(cancellationToken))
            {
                throw new AdminProvisioningException("管理員已存在。請使用 admin-reset 恢復存取權。");
            }

            var user = new AdminUser { UserName = username.Trim(), SingletonKey = 1, LockoutEnabled = true };
            cancellationToken.ThrowIfCancellationRequested();
            // The database's unique checked SingletonKey also protects competing initializations.
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
            // Recovery also clears lockout, in the same save as the password/security stamp.
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            cancellationToken.ThrowIfCancellationRequested();
            // Identity resets the security stamp atomically with the password hash.
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

// Its messages are intentionally safe to display; underlying database errors are not exposed.
public sealed class AdminProvisioningException(string message) : InvalidOperationException(message);
