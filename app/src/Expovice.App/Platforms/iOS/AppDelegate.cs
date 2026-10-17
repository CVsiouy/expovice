using Foundation;
using UIKit;

namespace Expovice.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    /// <summary>Routes expovice://auth#… back into WebAuthenticator.</summary>
    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options) =>
        Microsoft.Maui.Authentication.WebAuthenticator.Default.OpenUrl(url) || base.OpenUrl(application, url, options);
}
