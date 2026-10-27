using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Origami.Mobile.Navigation;
using Origami.Mobile.Services;

namespace Origami.Mobile.ViewModels;

/// <summary>編輯 API 基底網址；Android 模擬器需以 http://10.0.2.2:PORT 連到開發主機。</summary>
public sealed partial class SettingsViewModel(Navigator navigator, AppSettings settings) : ViewModelBase
{
    [ObservableProperty]
    public partial string BaseUrl { get; set; } = settings.BaseUrl;

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [RelayCommand]
    private void Save()
    {
        var candidate = BaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Message = "請輸入有效的 http:// 或 https:// 網址。";
            return;
        }
        settings.BaseUrl = candidate;
        settings.Save();
        navigator.Back();
    }
}
