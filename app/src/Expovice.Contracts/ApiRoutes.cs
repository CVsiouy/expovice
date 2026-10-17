namespace Expovice.Contracts;

/// <summary>Every HTTP route in one place so the API and the app's client can never drift apart.</summary>
public static class ApiRoutes
{
    public const string AuthOtpSend = "/auth/otp/send";
    public const string AuthOtpVerify = "/auth/otp/verify";
    public const string AuthRefresh = "/auth/refresh";
    public const string AuthSignOut = "/auth/signout";

    /// <summary>Browser flow for Google/Apple; finishes by redirecting to the app's callback scheme.</summary>
    public const string AuthExternalStart = "/auth/external/{provider}";

    public static string AuthExternal(ExternalProvider provider) => $"/auth/external/{provider.ToString().ToLowerInvariant()}";

    public const string Me = "/me";
    public const string MeName = "/me/name";
    public const string MeHome = "/me/home";
    public const string MePhoto = "/me/photos/{slot}";

    public static string MePhotoFor(PhotoSlot slot) => $"/me/photos/{slot}";

    public const string MeProvider = "/me/provider";
    public const string MeProviderAccepting = "/me/provider/accepting";

    public const string Providers = "/providers";
    public const string ProvidersTop = "/providers/top";
    public const string Provider = "/providers/{id:guid}";

    public static string ProviderFor(Guid id) => $"/providers/{id}";

    public const string Requests = "/requests";
    public const string RequestCancel = "/requests/{id:guid}/cancel";
    public const string RequestRate = "/requests/{id:guid}/rate";

    public static string RequestCancelFor(Guid id) => $"/requests/{id}/cancel";

    public static string RequestRateFor(Guid id) => $"/requests/{id}/rate";

    public const string WorkSummary = "/work/summary";
    public const string WorkRequests = "/work/requests";
    public const string WorkAccept = "/work/requests/{id:guid}/accept";
    public const string WorkDecline = "/work/requests/{id:guid}/decline";
    public const string WorkComplete = "/work/requests/{id:guid}/complete";

    public static string WorkActionFor(Guid id, string action) => $"/work/requests/{id}/{action}";

    public const string Health = "/healthz";
}

/// <summary>Query-string parameter names for provider search.</summary>
public static class SearchParams
{
    public const string Category = "category";
    public const string Latitude = "lat";
    public const string Longitude = "lon";
    public const string RadiusKm = "radiusKm";
    public const string Sort = "sort";
    public const string AvailableToday = "availableToday";
    public const string MaxRate = "maxRate";
}

/// <summary>Constants the app and API must agree on for the Google/Apple browser flow.</summary>
public static class AuthCallback
{
    public const string Scheme = "expovice";
    public const string Uri = "expovice://auth";
    public const string RefreshTokenKey = "refresh_token";
    public const string IsNewUserKey = "new_user";
}
