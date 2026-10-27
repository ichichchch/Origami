using UIKit;

namespace Origami.Mobile.iOS;

public class Application
{
    // 這是應用程式的主要入口點。
    static void Main(string[] args)
    {
        // 如果想使用有別於 "AppDelegate" 的應用程式委派類別，
        // 可以在這裡指定。
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
