namespace Expovice.App;

/// <summary>
/// Build-time configuration. Holds no secrets: the app only knows the public API URL; every key lives server-side
/// (Key Vault). Override per build with -p:DefineConstants or edit before publishing.
/// </summary>
public static class AppConfig
{
#if DEBUG
    /// <summary>Android emulators reach the host machine at 10.0.2.2; the iOS simulator shares the Mac's localhost.</summary>
    public static Uri ApiBaseUrl { get; } = new(DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5080" : "http://localhost:5080");
#else
    public static Uri ApiBaseUrl { get; } = new("https://api.expovice.app");
#endif
}
