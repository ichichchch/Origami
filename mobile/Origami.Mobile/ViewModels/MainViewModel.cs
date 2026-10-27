using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Origami.Mobile.Navigation;
using Origami.Mobile.Services;

namespace Origami.Mobile.ViewModels;

/// <summary>Published posts list with paging and hybrid search against /api/v1.</summary>
public sealed partial class MainViewModel(Navigator navigator, OrigamiApiClient api) : ViewModelBase
{
    private const int PageSize = 10;
    private int page;
    private int total;
    private bool inSearchMode;

    [ObservableProperty]
    public partial ObservableCollection<ItemModel> Items { get; set; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial string Mode { get; set; } = "最新文章";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool CanLoadMore { get; set; }

    public MainViewModel(Navigator navigator, OrigamiApiClient api, bool startLoaded)
        : this(navigator, api)
    {
        // Chained constructor runs first; load only when the shell asks for it.
        if (startLoaded) _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync() => await LoadAsync(page + 1);

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            await LoadAsync();
            return;
        }
        await RunBusyAsync(async ct =>
        {
            var result = await api.SearchAsync(query, PageSize, ct);
            inSearchMode = true;
            total = result.Hits.Count;
            page = 1;
            CanLoadMore = false;
            Mode = result.SemanticAvailable ? "搜尋（關鍵字＋語意）" : "搜尋（僅關鍵字，語意未設定）";
            Items = [.. result.Hits.Select(hit => new ItemModel(hit.Title, hit.Snippet, hit.Slug, hit.Source))];
            Message = result.Hits.Count == 0 ? "沒有符合的已發布內容。" : "";
        });
    }

    [RelayCommand]
    private void OpenItem(ItemModel item) => navigator.Push(new PostDetailViewModel(api, item.Slug, startLoaded: true));

    [RelayCommand]
    private void OpenSettings() => navigator.Push(new SettingsViewModel(navigator, AppScope.Settings));

    private async Task LoadAsync(int targetPage = 1)
    {
        await RunBusyAsync(async ct =>
        {
            var result = await api.ListPostsAsync(targetPage, PageSize, ct);
            inSearchMode = false;
            page = result.Page;
            total = result.Total;
            var loaded = (targetPage - 1) * PageSize + result.Items.Count;
            CanLoadMore = loaded < total;
            Mode = "最新文章";
            var mapped = result.Items.Select(item => new ItemModel(
                item.Title,
                (item.IsProtected ? "此內容受密碼保護，App 不提供正文。" : item.Excerpt),
                item.Slug,
                item.IsSticky ? "置頂" : item.IsProtected ? "保護" : ""));
            if (targetPage == 1) Items = [.. mapped];
            else foreach (var entry in mapped) Items.Add(entry);
            Message = result.Items.Count == 0 ? "目前沒有已發布的文章。" : "";
        });
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await work(CancellationToken.None);
        }
        catch (Exception error)
        {
            // Network and server failures surface as a message, never a crash.
            Message = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
