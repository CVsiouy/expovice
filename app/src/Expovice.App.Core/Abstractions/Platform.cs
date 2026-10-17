using Expovice.Contracts;

namespace Expovice.App.Core.Abstractions;

/// <summary>Shell navigation, abstracted so view models never reference MAUI.</summary>
public interface INavigationService
{
    /// <param name="route">A value from <see cref="Routes"/>; ".." goes back.</param>
    /// <param name="parameters">Objects handed to the target page's view model via <see cref="ViewModels.ViewModelBase.ApplyParameters"/>.</param>
    Task GoToAsync(string route, IDictionary<string, object>? parameters = null);

    Task GoBackAsync();
}

/// <summary>Persists the refresh token (secure storage) and simple flags (preferences).</summary>
public interface ISessionStore
{
    Task<string?> GetRefreshTokenAsync();

    Task SetRefreshTokenAsync(string? refreshToken);

    bool HasSeenOnboarding { get; set; }
}

/// <summary>Where the customer is. Area is the locality shown in copy like "within 5 km of Indiranagar".</summary>
public sealed record UserLocation(double Latitude, double Longitude, string Line, string Area);

public interface ILocationService
{
    /// <summary>Never throws: falls back to a default location when permission is denied or GPS is unavailable.</summary>
    Task<UserLocation> GetCurrentAsync(CancellationToken ct = default);
}

/// <summary>Result of the Google/Apple browser round-trip: a one-time refresh token the app exchanges immediately.</summary>
public sealed record ExternalAuthResult(string RefreshToken, bool IsNewUser);

public interface IExternalAuthService
{
    /// <returns>null when the user cancelled.</returns>
    Task<ExternalAuthResult?> AuthenticateAsync(ExternalProvider provider);
}

/// <summary>A photo the user picked, already buffered so uploads can be retried after a token refresh.</summary>
public sealed record PickedPhoto(byte[] Bytes, string ContentType, string FileName);

public interface IPhotoPicker
{
    /// <returns>null when the user cancelled.</returns>
    Task<PickedPhoto?> PickAsync();
}

public interface IDialogService
{
    Task ToastAsync(string message);
}
