using Expovice.App.Core.Abstractions;
using Expovice.App.Core.Api;
using Expovice.App.Core.Services;
using Microsoft.Extensions.Logging;

namespace Expovice.App;

public partial class App : Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services, AuthSession session, UserContext user, INavigationService navigation, ILogger<App> logger)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light; // the Organic system is a light, warm ground only
        this.services = services;

        // Refresh token rejected (expired/revoked) → back to sign-in. Raised off the UI thread.
        session.SessionExpired += (_, _) => MainThread.BeginInvokeOnMainThread(async () =>
        {
            logger.LogInformation("Session expired; returning to sign-in");
            user.Reset();
            await navigation.GoToAsync(Routes.SignIn);
        });
    }

    /// <summary>The shell is resolved here, not injected, so it is built after App.xaml's resources exist.</summary>
    protected override Window CreateWindow(IActivationState? activationState) => new(services.GetRequiredService<AppShell>());
}
