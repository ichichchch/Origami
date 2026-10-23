using Microsoft.AspNetCore.Components;
using Origami.Web.Services;

namespace Origami.Web.Components.Admin;

public abstract class AdminComponentBase : ComponentBase, IDisposable
{
    [Inject] protected ILogger<AdminComponentBase> Logger { get; set; } = default!;
    protected readonly CancellationTokenSource Lifetime = new();
    protected bool Busy { get; private set; }
    protected bool LoginRequired { get; private set; }
    protected string? Error { get; set; }
    protected string? Notice { get; set; }
    protected bool Blocked => Busy || LoginRequired;

    protected void RequireLogin()
    {
        LoginRequired = true;
        Error = "登入已失效，請重新登入。未儲存的輸入仍保留在此頁，離開前請先複製備份。";
        Notice = null;
    }

    protected async Task<bool> RunAsync(Func<CancellationToken, Task> action, string? success = null)
    {
        if (Blocked || Lifetime.IsCancellationRequested) return false;
        Busy = true;
        Error = null;
        Notice = null;
        try
        {
            await action(Lifetime.Token);
            Notice = success;
            return true;
        }
        catch (UnauthorizedAccessException) { RequireLogin(); }
        catch (CmsException ex) { Error = ex.Message; }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "CMS component operation failed");
            Error = "操作未完成，無法確認是否已儲存。請保留輸入並重新載入，確認最新資料後再試。";
        }
        finally { Busy = false; }
        return false;
    }

    public virtual void Dispose()
    {
        Lifetime.Cancel();
        Lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
