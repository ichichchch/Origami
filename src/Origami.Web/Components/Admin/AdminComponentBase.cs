namespace Origami.Web.Components.Admin;

public abstract partial class AdminComponentBase : ComponentBase, IDisposable
{
    [Inject] protected ILogger<AdminComponentBase> Logger { get; set; } = default!;
    [Inject] protected OrigamiClient AccessApi { get; set; } = default!;
    protected CancellationTokenSource Lifetime { get; } = new();
    protected bool Busy { get; private set; }
    protected bool LoginRequired { get; private set; }
    protected string? Error { get; set; }
    protected string? Notice { get; set; }
    protected SiteAccessInfo? Access { get; private set; }
    protected bool CanEdit => Access?.CanEdit == true;
    protected bool CanAdminister => Access?.CanAdminister == true;
    protected bool IsNetworkAdmin => Access?.IsNetworkAdmin == true;
    protected virtual bool UsesSiteAccess => true;
    protected virtual bool RequiresMembership => true;
    // 唯讀限制不可放進 Blocked：Reader 仍須能載入、搜尋及分頁。
    protected bool Blocked => Busy || LoginRequired || !RendererInfo.IsInteractive;
    protected bool EditBlocked => Blocked || !CanEdit;
    protected bool AdministrationBlocked => Blocked || !CanAdminister;

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
            if (UsesSiteAccess)
            {
                Access = await AccessApi.GetAccessAsync(Lifetime.Token);
                if (RequiresMembership && !Access.CanRead) throw new AccessDeniedException();
            }
            await action(Lifetime.Token);
            Notice = success;
            return true;
        }
        catch (UnauthorizedAccessException) { RequireLogin(); }
        catch (AccessDeniedException ex) { Access = null; Error = ex.Message; }
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

    protected Task<bool> RunEditAsync(Func<CancellationToken, Task> action, string? success = null) =>
        RunAsync(async ct =>
        {
            if (!CanEdit) throw new AccessDeniedException("你只有本站唯讀權限，無法建立、修改或發布內容。");
            await action(ct);
        }, success);

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
