using CommunityToolkit.Maui.Alerts;
using Expovice.App.Core.Abstractions;
using Expovice.Contracts;
using Microsoft.Extensions.Logging;

namespace Expovice.App.Services;

/// <summary>Shell navigation, always marshalled to the UI thread (callers may be on a background thread).</summary>
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null) =>
        MainThread.InvokeOnMainThreadAsync(() => parameters is null
            ? Shell.Current.GoToAsync(route)
            : Shell.Current.GoToAsync(route, new ShellNavigationQueryParameters(parameters)));

    public Task GoBackAsync() => GoToAsync(Routes.Back);
}

/// <summary>Refresh token in the OS keychain/keystore (SecureStorage); flags in Preferences.</summary>
public sealed class SessionStore(ILogger<SessionStore> logger) : ISessionStore
{
    private const string RefreshTokenKey = "expovice.refresh";
    private const string OnboardingKey = "expovice.onboarded";

    public async Task<string?> GetRefreshTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(RefreshTokenKey);
        }
        catch (Exception ex)
        {
            // SecureStorage can throw if the keystore was reset (e.g. after a backup restore); treat as signed out.
            logger.LogWarning(ex, "Could not read secure storage");
            SecureStorage.Default.Remove(RefreshTokenKey);
            return null;
        }
    }

    public Task SetRefreshTokenAsync(string? refreshToken)
    {
        if (refreshToken is null)
        {
            SecureStorage.Default.Remove(RefreshTokenKey);
            return Task.CompletedTask;
        }

        return SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
    }

    public bool HasSeenOnboarding
    {
        get => Preferences.Default.Get(OnboardingKey, false);
        set => Preferences.Default.Set(OnboardingKey, value);
    }
}

/// <summary>GPS + reverse geocoding, falling back to Indiranagar (the design's sample area) when unavailable.</summary>
public sealed class LocationService(ILogger<LocationService> logger) : ILocationService
{
    public static readonly UserLocation Fallback = new(12.9719, 77.6412, "12th Main, Indiranagar, Bengaluru", "Indiranagar");

    public async Task<UserLocation> GetCurrentAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.LocationWhenInUse>);
            if (status != PermissionStatus.Granted)
            {
                return Fallback;
            }

            var location = await Geolocation.Default.GetLastKnownLocationAsync()
                ?? await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)), ct);
            if (location is null)
            {
                return Fallback;
            }

            var place = (await Geocoding.Default.GetPlacemarksAsync(location))?.FirstOrDefault();
            var street = string.Join(' ', new[] { place?.SubThoroughfare, place?.Thoroughfare }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var parts = new[] { street, place?.SubLocality, place?.Locality }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            var area = place?.SubLocality ?? place?.Locality ?? Fallback.Area;
            return new UserLocation(location.Latitude, location.Longitude, parts.Length > 0 ? string.Join(", ", parts) : area, area);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Location APIs throw a family of platform exceptions (not supported, disabled, permission, geocoder I/O);
            // none should block the app, so all fall back to the default area.
            logger.LogWarning(ex, "Location unavailable; using fallback");
            return Fallback;
        }
    }
}

/// <summary>
/// Google/Apple sign-in through the system browser. The API runs the OAuth exchange and redirects to
/// expovice://auth#refresh_token=…&amp;new_user=…; that refresh token is single-use (rotated on first refresh).
/// </summary>
public sealed class ExternalAuthService(ILogger<ExternalAuthService> logger) : IExternalAuthService
{
    public async Task<ExternalAuthResult?> AuthenticateAsync(ExternalProvider provider)
    {
        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(AppConfig.ApiBaseUrl, ApiRoutes.AuthExternal(provider)),
                CallbackUrl = new Uri(AuthCallback.Uri),
                PrefersEphemeralWebBrowserSession = true,
            });
            if (!result.Properties.TryGetValue(AuthCallback.RefreshTokenKey, out var refresh) || string.IsNullOrEmpty(refresh))
            {
                logger.LogWarning("{Provider} callback had no refresh token", provider);
                return null;
            }

            var isNew = result.Properties.TryGetValue(AuthCallback.IsNewUserKey, out var n) && n == "true";
            return new ExternalAuthResult(refresh, isNew);
        }
        catch (TaskCanceledException)
        {
            return null; // user closed the browser sheet
        }
    }
}

/// <summary>Picks one photo from the gallery and buffers it (uploads must be replayable after a token refresh).</summary>
public sealed class PhotoPicker(ILogger<PhotoPicker> logger) : IPhotoPicker
{
    public async Task<PickedPhoto?> PickAsync()
    {
        try
        {
            var files = await MainThread.InvokeOnMainThreadAsync(() =>
                MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "Choose a photo", SelectionLimit = 1 }));
            var file = files?.FirstOrDefault();
            if (file is null)
            {
                return null;
            }

            await using var stream = await file.OpenReadAsync();
            if (stream.CanSeek && stream.Length > Limits.MaxPhotoBytes)
            {
                await Toast.Make($"Photos must be under {Limits.MaxPhotoBytes / (1024 * 1024)} MB").Show();
                return null;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return new PickedPhoto(buffer.ToArray(), file.ContentType ?? "image/jpeg", file.FileName);
        }
        catch (PermissionException ex)
        {
            logger.LogWarning(ex, "Photo permission denied");
            await Toast.Make("Allow photo access in Settings to add photos").Show();
            return null;
        }
    }
}

public sealed class DialogService : IDialogService
{
    public Task ToastAsync(string message) =>
        MainThread.InvokeOnMainThreadAsync(() => Toast.Make(message).Show());
}
