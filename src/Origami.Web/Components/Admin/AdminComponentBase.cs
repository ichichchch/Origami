namespace Origami.Web.Components.Admin;

public abstract partial class AdminComponentBase : ComponentBase, IDisposable
{
    [Inject] protected ILogger<AdminComponentBase> Logger { get; set; } = default!;
    protected CancellationTokenSource Lifetime { get; } = new();
    protected bool Busy { get; private set; }
    protected bool LoginRequired { get; private set; }
    protected string? Error { get; set; }
    protected string? Notice { get; set; }
    protected bool Blocked => Busy || LoginRequired || !RendererInfo.IsInteractive;

    protected void RequireLogin()
    {
        LoginRequired = true;
        Error = "登入已失效，請重新登入。未儲存的輸入仍保留在此頁，離開前請先複製備份。";
        Notice = null;
    }

    protected async Task<bool> RunAsync(Func<CancellationToken, Task> action, string? success = null)
    {
        // 初始資料載入在預渲染期間也會執行；僅使用者控制項需等待互動模式。
        if (Busy || LoginRequired || Lifetime.IsCancellationRequested) return false;
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
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { /* 元件在操作進行中被處置；使用者輸入刻意保持原狀。 */ }
        catch (Exception ex)
        {
            Log.ComponentOperationFailed(Logger, ex);
            Error = "操作未完成，無法確認是否已儲存。請保留輸入並重新載入，確認最新資料後再試。";
        }
        finally { Busy = false; }
        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Error, "CMS component operation failed")]
        public static partial void ComponentOperationFailed(ILogger logger, Exception exception);
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Lifetime.Cancel();
            Lifetime.Dispose();
        }
    }
}
