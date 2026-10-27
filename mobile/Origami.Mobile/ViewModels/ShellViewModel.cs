using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Origami.Mobile.Navigation;

namespace Origami.Mobile.ViewModels;

/// <summary>Root shell: hosts the navigator's current page and the back button.</summary>
public sealed partial class ShellViewModel(Navigator navigator) : ViewModelBase
{
    public Navigator Navigator { get; } = navigator;

    [RelayCommand]
    private void Back() => Navigator.Back();
}
