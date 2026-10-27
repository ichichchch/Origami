using System;
using Avalonia;

namespace Origami.Mobile.Desktop;

sealed class Program
{
    // 初始化程式碼。在呼叫 AppMain 之前，請勿使用任何 Avalonia、第三方 API
    // 或任何依賴 SynchronizationContext 的程式碼：此時尚未完成初始化，
    // 可能導致程式出錯。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia 設定，請勿移除；視覺化設計工具亦會使用。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
