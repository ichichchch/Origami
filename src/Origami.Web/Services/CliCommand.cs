namespace Origami.Web.Services;

public static class CliCommand
{
    public static Task<int> RunAsync(IServiceProvider services, string command) =>
        RunAsync(services, command, new SystemCliConsole());

    internal static async Task<int> RunAsync(IServiceProvider services, string command, ICliConsole console)
    {
        if (command is not ("export" or "import" or "create-user"))
        {
            await console.Error.WriteLineAsync("請使用 export、import 或 create-user。");
            return 2;
        }
        if (console.IsInputRedirected)
        {
            await console.Error.WriteLineAsync("此命令需要互動終端機；不接受重導向的輸入。");
            return 2;
        }

        try
        {
            var session = services.GetRequiredService<CliSession>();
            if (!session.IsAuthenticated) await AuthenticateAsync(services, console);
            return command switch
            {
                "export" => await ExportAsync(services, console),
                "import" => await ImportAsync(services, console),
                _ => await CreateUserAsync(services, console)
            };
        }
        catch (CliCommandException error)
        {
            await console.Error.WriteLineAsync(error.Message);
            return 1;
        }
        catch (CmsException error)
        {
            await console.Error.WriteLineAsync(error.Message);
            return 1;
        }
        catch (AdminProvisioningException error)
        {
            await console.Error.WriteLineAsync(error.Message);
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            await console.Error.WriteLineAsync("管理員認證失敗或工作階段已失效。");
            return 1;
        }
        catch (OperationCanceledException)
        {
            await console.Error.WriteLineAsync("命令已取消。");
            return 1;
        }
        catch (Exception)
        {
            await console.Error.WriteLineAsync("命令執行失敗，請檢查終端機、資料庫與儲存設定。");
            return 1;
        }
    }

    private static async Task AuthenticateAsync(IServiceProvider services, ICliConsole console)
    {
        var username = ReadRequired(console, "全站管理者帳號：", AdminProvisioner.MaximumUserNameLength);
        var password = console.ReadPassword("密碼：", AdminProvisioner.MaximumPasswordLength);
        var signIn = services.GetRequiredService<SignInManager<AdminUser>>();
        var user = await signIn.UserManager.FindByNameAsync(username);
        if (user is null || user.IsDisabled || user.SingletonKey != 1)
            throw new CliCommandException("管理員認證失敗。");
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut) throw new CliCommandException("管理員帳號已鎖定，請稍後再試。");
        if (result.RequiresTwoFactor)
        {
            var code = ReadRequired(console, "兩步驟驗證碼（或備用恢復碼）：", 10);
            var totp = !string.IsNullOrEmpty(user.TwoFactorSecret) && Totp.Verify(user.TwoFactorSecret, code,
                services.GetRequiredService<TimeProvider>());
            var recovery = !totp && TwoFactorService.TryRedeemRecoveryCode(user, code);
            if (!totp && !recovery)
            {
                await signIn.UserManager.AccessFailedAsync(user);
                throw new CliCommandException("兩步驟驗證失敗。");
            }
            if (recovery)
            {
                var updated = await signIn.UserManager.UpdateAsync(user);
                if (!updated.Succeeded) throw new CliCommandException("無法更新備用恢復碼，請稍後再試。");
            }
        }
        else if (!result.Succeeded)
        {
            throw new CliCommandException("管理員認證失敗。");
        }
        var reset = await signIn.UserManager.ResetAccessFailedCountAsync(user);
        if (!reset.Succeeded) throw new CliCommandException("無法完成管理員認證，請稍後再試。");
        services.GetRequiredService<CliSession>().SignIn(user, services.GetRequiredService<IOptions<IdentityOptions>>().Value,
            services.GetRequiredService<TimeProvider>());
    }

    private static async Task<int> ExportAsync(IServiceProvider services, ICliConsole console)
    {
        console.WriteLine("選擇格式：1＝JSON（文字資料與媒體中繼資料），2＝ZIP（另含媒體檔案）。");
        var format = console.ReadLine("格式 [2]：")?.Trim() ?? "";
        var archive = format is "2" or "";
        if (!archive && format != "1") throw new CliCommandException("格式請輸入 1 或 2。");
        var path = ReadOutputPath(console);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = File.Create(temporary))
            {
                var exporter = services.GetRequiredService<ExportService>();
                if (archive) await exporter.WriteArchiveAsync(output);
                else await exporter.WriteAsync(output);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        console.WriteLine($"已完成預設站點匯出：{path}");
        return 0;
    }

    private static async Task<int> ImportAsync(IServiceProvider services, ICliConsole console)
    {
        var path = ReadInputPath(console);
        await using var input = File.OpenRead(path);
        var magic = new byte[2];
        var read = await input.ReadAsync(magic.AsMemory());
        input.Position = 0;
        var archive = read == 2 && magic[0] == (byte)'P' && magic[1] == (byte)'K';
        var importer = services.GetRequiredService<ImportService>();
        var preview = await importer.AnalyzeAsync(input);
        input.Position = 0;
        console.WriteLine($"匯入站點：{preview.SiteName}");
        console.WriteLine($"分類 {preview.Categories}、標籤 {preview.Tags}、文章 {preview.Posts}、頁面 {preview.Pages}、媒體 {preview.Media}。");
        console.WriteLine(archive
            ? "此操作會以檔案取代預設站點的文字資料與媒體；ZIP 清單外的當站圖片將被清除。"
            : "此操作會以檔案取代預設站點的設定、分類標籤、內容與媒體中繼資料；JSON 不含媒體檔案。");
        var confirmation = ReadRequired(console, "請輸入 REPLACE 確認：", 7);
        if (confirmation != "REPLACE") throw new CliCommandException("未確認，未做任何變更。");
        await importer.ApplyAsync(input, confirmed: true);
        console.WriteLine("預設站點匯入完成。");
        return 0;
    }

    private static async Task<int> CreateUserAsync(IServiceProvider services, ICliConsole console)
    {
        var username = ReadRequired(console, "新使用者帳號：", AdminProvisioner.MaximumUserNameLength);
        console.WriteLine("電子郵件可留空；未提供電子郵件的帳號無法自助重設密碼。");
        var email = console.ReadLine("電子郵件：")?.Trim() ?? "";
        if (email.Length > 256) throw new CliCommandException("電子郵件不得超過 256 字。");
        console.WriteLine("密碼至少 8 字，需含大寫、小寫、數字、符號，且至少有四個不同字元。");
        var password = console.ReadPassword("密碼：", AdminProvisioner.MaximumPasswordLength);
        var confirmation = console.ReadPassword("再次輸入密碼：", AdminProvisioner.MaximumPasswordLength);
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            throw new CliCommandException("兩次密碼不一致，未建立帳號。");
        var user = await services.GetRequiredService<SiteAdministrationService>().CreateUserAsync(
            new UserCreateInput { UserName = username, Email = email, Password = password });
        console.WriteLine($"使用者已建立：{user.UserName}（{user.Id}）。");
        return 0;
    }

    private static string ReadOutputPath(ICliConsole console)
    {
        var path = ReadRequired(console, "匯出檔案路徑：", 4096);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new CliCommandException("匯出目錄不存在。");
        if (File.Exists(fullPath))
        {
            var overwrite = ReadRequired(console, "檔案已存在，輸入 OVERWRITE 確認覆寫：", 8);
            if (overwrite != "OVERWRITE") throw new CliCommandException("未確認覆寫，未做任何變更。");
        }
        return fullPath;
    }

    private static string ReadInputPath(ICliConsole console)
    {
        var path = Path.GetFullPath(ReadRequired(console, "匯入檔案路徑：", 4096));
        if (!File.Exists(path)) throw new CliCommandException("匯入檔案不存在。");
        return path;
    }

    private static string ReadRequired(ICliConsole console, string prompt, int maximum)
    {
        var value = console.ReadLine(prompt) ?? "";
        if (string.IsNullOrWhiteSpace(value)) throw new CliCommandException("輸入不可為空白。");
        if (value.Length > maximum) throw new CliCommandException($"輸入不得超過 {maximum} 字。");
        return value.Trim();
    }

    internal interface ICliConsole
    {
        bool IsInputRedirected { get; }
        void Write(string value);
        void WriteLine(string value);
        TextWriter Error { get; }
        string? ReadLine(string prompt);
        string ReadPassword(string prompt, int maximum);
    }

    private sealed class SystemCliConsole : ICliConsole
    {
        public bool IsInputRedirected => Console.IsInputRedirected;
        public TextWriter Error => Console.Error;
        public void Write(string value) => Console.Write(value);
        public void WriteLine(string value) => Console.WriteLine(value);
        public string? ReadLine(string prompt)
        {
            Console.Write(prompt);
            return Console.ReadLine();
        }
        public string ReadPassword(string prompt, int maximum) => AdminCommand.ReadPassword(prompt);
    }

    private sealed class CliCommandException(string message) : Exception(message);
}
