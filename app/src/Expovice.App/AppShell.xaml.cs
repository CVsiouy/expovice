using Expovice.App.Core.Abstractions;
using Expovice.App.Pages;

namespace Expovice.App;

public partial class AppShell : Shell
{
    /// <summary>Pushed route → page type. Pages are resolved from DI so their view models are injected.</summary>
    private static readonly Dictionary<string, Type> PushedPages = new()
    {
        [Routes.Otp] = typeof(OtpPage),
        [Routes.Name] = typeof(NamePage),
        [Routes.Listing] = typeof(ListingPage),
        [Routes.Detail] = typeof(ProviderDetailPage),
        [Routes.Request] = typeof(RequestPage),
        [Routes.Sent] = typeof(RequestSentPage),
        [Routes.Setup] = typeof(SetupPage),
        [Routes.Live] = typeof(LivePage),
    };

    public AppShell()
    {
        InitializeComponent();
        foreach (var route in Routes.Pushed)
        {
            Routing.RegisterRoute(route, PushedPages[route]);
        }
    }
}
