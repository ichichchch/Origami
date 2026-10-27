using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Origami.Mobile.Navigation;

namespace Origami.Mobile.ViewModels;

/// <summary>根殼層：承載導覽器目前的頁面與返回按鈕。</summary>
public sealed partial class ShellViewModel(Navigator navigator) : ViewModelBase
{
    public Navigator Navigator { get; } = navigator;

    [RelayCommand]
    private void Back() => Navigator.Back();
}
