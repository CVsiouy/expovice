using Android.App;
using Android.Content;
using Android.Content.PM;
using Expovice.Contracts;

namespace Expovice.App;

/// <summary>Receives expovice://auth#… after Google/Apple sign-in and hands it back to WebAuthenticator.</summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = AuthCallback.Scheme,
    DataHost = "auth")]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
}
