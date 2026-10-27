using Foundation;
using UIKit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;

namespace Origami.Mobile.iOS;

// 應用程式的 UIApplicationDelegate。此類別負責啟動應用程式的
// 使用者介面，並負責聆聽（且可選擇回應）
// 來自 iOS 的應用程式事件。
[Register("AppDelegate")]
#pragma warning disable CA1711 // 識別字不應使用不正確的後置詞
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // 識別字不應使用不正確的後置詞
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
