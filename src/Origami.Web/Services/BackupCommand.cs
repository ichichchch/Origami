namespace Origami.Web.Services;

public static class BackupCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        try
        {
            var completed = await services.GetRequiredService<BackupWorker>().RunOnceAsync();
            if (completed == 0)
            {
                await Console.Error.WriteLineAsync("沒有完成任何站點備份，請檢查伺服器日誌與 WebDAV 設定。");
                return 1;
            }
            Console.WriteLine($"已完成 {completed} 個站點的本機備份與已設定的異地備份。");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("備份已取消。");
            return 1;
        }
        catch (Exception)
        {
            await Console.Error.WriteLineAsync("備份執行失敗，請檢查伺服器日誌與儲存／WebDAV 設定。");
            return 1;
        }
    }
}
