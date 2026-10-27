using CommunityToolkit.Mvvm.ComponentModel;
using Origami.Mobile.Services;

namespace Origami.Mobile.ViewModels;

/// <summary>文章或頁面詳情。草稿、受保護與私密內容會顯示找不到內容的訊息。</summary>
public sealed partial class PostDetailViewModel(OrigamiApiClient api, string slug, string kind = "post") : ViewModelBase
{
    [ObservableProperty]
    public partial string Title { get; set; } = "載入中…";

    [ObservableProperty]
    public partial string Meta { get; set; } = "";

    [ObservableProperty]
    public partial string Markdown { get; set; } = "";

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    public PostDetailViewModel(OrigamiApiClient api, string slug, string kind = "post", bool startLoaded = false)
        : this(api, slug, kind)
    {
        if (startLoaded) _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var content = await api.GetContentAsync(kind, slug);
            if (content is null)
            {
                Title = "找不到內容";
                Message = "此內容可能未發布、已撤回，或受密碼保護與私密設定而不提供給 App。";
                return;
            }
            Title = content.Title;
            Markdown = content.Markdown;
            var parts = new List<string>();
            if (content.PublishedAt is { } published) parts.Add(published.LocalDateTime.ToString("yyyy-MM-dd"));
            if (content.Categories.Count > 0) parts.Add(string.Join("、", content.Categories.Select(c => c.Name)));
            if (content.Tags.Count > 0) parts.Add("#" + string.Join(" #", content.Tags.Select(t => t.Name)));
            Meta = string.Join("  ·  ", parts);
        }
        catch (Exception error)
        {
            Title = "載入失敗";
            Message = error.Message;
        }
    }
}
