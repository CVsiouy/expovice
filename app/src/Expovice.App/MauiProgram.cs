using CommunityToolkit.Maui;
using Expovice.App.Core;
using Expovice.App.Core.Abstractions;
using Expovice.App.Pages;
using Expovice.App.Services;
using Microsoft.Extensions.Logging;

namespace Expovice.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Caprasimo-Regular.ttf", "Caprasimo");
                fonts.AddFont("Figtree-Regular.ttf", "Figtree");
                fonts.AddFont("Figtree-SemiBold.ttf", "FigtreeSemiBold");
                fonts.AddFont("Figtree-Bold.ttf", "FigtreeBold");
            });

#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        PlatformStyling.RemoveNativeInputChrome();

        var services = builder.Services;
        services.AddExpoviceCore(AppConfig.ApiBaseUrl);

        // Platform implementations of Core abstractions.
        services.AddSingleton<INavigationService, ShellNavigationService>();
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<ILocationService, LocationService>();
        services.AddSingleton<IExternalAuthService, ExternalAuthService>();
        services.AddSingleton<IPhotoPicker, PhotoPicker>();
        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton<AppShell>();
        services.AddTransient<SplashPage>();
        services.AddTransient<OnboardingPage>();
        services.AddTransient<PhoneSignInPage>();
        services.AddTransient<OtpPage>();
        services.AddTransient<NamePage>();
        services.AddTransient<HomePage>();
        services.AddTransient<ListingPage>();
        services.AddTransient<ProviderDetailPage>();
        services.AddTransient<RequestPage>();
        services.AddTransient<RequestSentPage>();
        services.AddTransient<MyRequestsPage>();
        services.AddTransient<WorkPage>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<SetupPage>();
        services.AddTransient<LivePage>();

        return builder.Build();
    }
}
