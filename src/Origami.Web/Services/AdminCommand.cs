namespace Origami.Web.Services;

public static class AdminCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string command)
    {
        if (command is not ("admin-init" or "admin-reset"))
        {
            Console.Error.WriteLine("請使用 admin-init 或 admin-reset，且不可帶入憑證參數。");
            return 2;
        }
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("管理命令需要互動終端機；不接受重導向的輸入。");
            return 2;
        }

        try
        {
            string? username = null;
            if (command == "admin-init")
            {
                Console.Write("管理員帳號：");
                username = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(username) || username.Trim().Length > AdminProvisioner.MaximumUserNameLength)
                {
                    Console.Error.WriteLine("請輸入 1 至 256 字元的帳號。");
                    return 1;
                }
            }

            Console.WriteLine("密碼至少 12 字，需含大寫、小寫、數字、符號，且至少有四個不同字元。");
            var password = ReadPassword("密碼：");
            var confirmation = ReadPassword("再次輸入密碼：");
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("兩次密碼不一致，未做任何變更。");
                return 1;
            }

            await using var scope = services.CreateAsyncScope();
            var provisioner = scope.ServiceProvider.GetRequiredService<AdminProvisioner>();
            if (command == "admin-init")
            {
                await provisioner.InitializeAsync(username!, password);
                Console.WriteLine("管理員已建立。");
            }
            else
            {
                await provisioner.ResetPasswordAsync(password);
                Console.WriteLine("密碼已重設，先前的登入已失效。");
            }
            return 0;
        }
        catch (AdminProvisioningException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("管理命令已取消。");
            return 1;
        }
        catch (Exception)
        {
            // Never print exception details, command arguments, passwords, or connection strings.
            Console.Error.WriteLine("管理命令執行失敗。請檢查終端機、應用設定與已套用的資料庫遷移。");
            return 1;
        }
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var buffer = new char[AdminProvisioner.MaximumPasswordLength];
        var length = 0;
        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return new string(buffer, 0, length);
                }
                if (key.Key == ConsoleKey.Escape || key.KeyChar == '\u0003')
                {
                    Console.WriteLine();
                    throw new OperationCanceledException();
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (length > 0)
                    {
                        buffer[--length] = '\0';
                    }
                    continue;
                }
                if (char.IsControl(key.KeyChar))
                {
                    continue;
                }
                if (length == buffer.Length)
                {
                    Console.WriteLine();
                    throw new AdminProvisioningException("密碼超過 1024 字上限，未做任何變更。");
                }
                buffer[length++] = key.KeyChar;
            }
        }
        finally
        {
            Array.Clear(buffer);
        }
    }
}
