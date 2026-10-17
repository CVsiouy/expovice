namespace Expovice.App.Core.Abstractions;

/// <summary>Shell routes. "//" routes are absolute (they reset the stack); the rest are pushed.</summary>
public static class Routes
{
    public const string Splash = "//splash";
    public const string Onboarding = "//onboarding";
    public const string SignIn = "//signin";
    public const string Home = "//main/home";
    public const string Requests = "//main/requests";
    public const string Work = "//main/work";
    public const string Profile = "//main/profile";

    public const string Otp = "otp";
    public const string Name = "name";
    public const string Listing = "listing";
    public const string Detail = "detail";
    public const string Request = "request";
    public const string Sent = "sent";
    public const string Setup = "setup";
    public const string Live = "live";

    /// <summary>Replace the current page with Live (pop Setup, push Live).</summary>
    public const string SetupToLive = "../live";

    public const string Back = "..";

    /// <summary>Pushed routes registered with Routing.RegisterRoute in the app.</summary>
    public static IReadOnlyList<string> Pushed { get; } = [Otp, Name, Listing, Detail, Request, Sent, Setup, Live];
}

/// <summary>Navigation parameter keys.</summary>
public static class NavParams
{
    public const string PhoneNumber = "phone";
    public const string CategoryId = "category";
    public const string ProviderId = "providerId";
    public const string Provider = "provider";
    public const string IsNewUser = "isNewUser";
}
