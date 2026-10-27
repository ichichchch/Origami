using Origami.Mobile.Navigation;
using Origami.Mobile.Services;
using Origami.Mobile.ViewModels;

namespace Origami.Mobile.Services;

/// <summary>Composes the single-instance app services and the root shell view model.</summary>
public static class AppScope
{
    public static Navigator Navigator { get; } = new();

    public static AppSettings Settings { get; } = AppSettings.Load();

    public static OrigamiApiClient Api { get; } = new(Settings);

    public static ShellViewModel CreateShell()
    {
        var shell = new ShellViewModel(Navigator);
        Navigator.Push(new MainViewModel(Navigator, Api, startLoaded: true));
        return shell;
    }
}
