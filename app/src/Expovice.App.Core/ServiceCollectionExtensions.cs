using Expovice.App.Core.Api;
using Expovice.App.Core.Services;
using Expovice.App.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Expovice.App.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the API client pipeline, shared state and every view model. Platform services
    /// (INavigationService, ISessionStore, ...) are registered by the MAUI app.
    /// </summary>
    public static IServiceCollection AddExpoviceCore(this IServiceCollection services, Uri apiBaseUrl)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuthSession>();
        services.AddSingleton<UserContext>();
        services.AddSingleton<SignInFlow>();
        services.AddTransient<AuthTokenHandler>();

        // Refresh calls go through a bare client (no token handler) so refreshing can never recurse.
        services.AddHttpClient(AuthSession.RefreshClientName, c => c.BaseAddress = apiBaseUrl)
            .AddStandardResilienceHandler(ConfigureResilience);

        services.AddHttpClient<IExpoviceApi, ExpoviceApiClient>(c => c.BaseAddress = apiBaseUrl)
            .AddHttpMessageHandler<AuthTokenHandler>()
            .AddStandardResilienceHandler(ConfigureResilience);

        services.AddTransient<SplashViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<PhoneSignInViewModel>();
        services.AddTransient<OtpViewModel>();
        services.AddTransient<NameViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ListingViewModel>();
        services.AddTransient<ProviderDetailViewModel>();
        services.AddTransient<RequestViewModel>();
        services.AddTransient<RequestSentViewModel>();
        services.AddTransient<MyRequestsViewModel>();
        services.AddTransient<WorkViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<SetupViewModel>();
        services.AddTransient<LiveViewModel>();
        return services;
    }

    /// <summary>Retries only GET/HEAD/OPTIONS etc.: replaying a POST could, say, send a request to a pro twice.</summary>
    private static void ConfigureResilience(HttpStandardResilienceOptions options) =>
        options.Retry.DisableForUnsafeHttpMethods();
}
