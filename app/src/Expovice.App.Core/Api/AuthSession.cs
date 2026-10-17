using System.Net;
using System.Net.Http.Json;
using Expovice.App.Core.Abstractions;
using Expovice.Contracts;
using Microsoft.Extensions.Logging;

namespace Expovice.App.Core.Api;

/// <summary>
/// Holds the short-lived access token in memory and rotates it with the refresh token kept in secure storage.
/// Thread-safe: refreshes are serialised by a SemaphoreSlim, so N concurrent 401s cause exactly one /auth/refresh call.
/// </summary>
public sealed class AuthSession(IHttpClientFactory httpClientFactory, ISessionStore store, ILogger<AuthSession> logger) : IDisposable
{
    /// <summary>Named HttpClient WITHOUT <see cref="AuthTokenHandler"/>, so refreshing can never recurse.</summary>
    public const string RefreshClientName = "Expovice.Auth";

    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile string? accessToken;

    /// <summary>Raised (on a background thread) when the refresh token is rejected; the app navigates to sign-in.</summary>
    public event EventHandler? SessionExpired;

    public string? AccessToken => accessToken;

    public bool IsSignedIn => accessToken is not null;

    /// <summary>Stores tokens returned by OTP verification.</summary>
    public async Task SignInAsync(AuthResponse response)
    {
        await gate.WaitAsync();
        try
        {
            await StoreAsync(response);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Exchanges the one-time refresh token from the Google/Apple browser flow for a full session.</summary>
    public async Task SignInWithRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var response = await CallRefreshAsync(refreshToken, ct)
                ?? throw new ApiException(HttpStatusCode.Unauthorized, "Sign-in expired. Please try again.");
            await StoreAsync(response);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Called at app start: true when a stored refresh token still works.</summary>
    public Task<bool> TryRestoreAsync(CancellationToken ct = default) => RefreshAsync(null, raiseExpired: false, ct);

    /// <summary>
    /// Refreshes after <paramref name="failedAccessToken"/> got a 401. If another caller already replaced that token
    /// while we waited on the gate, returns true without a network call.
    /// </summary>
    public Task<bool> RefreshAsync(string? failedAccessToken, CancellationToken ct = default) =>
        RefreshAsync(failedAccessToken, raiseExpired: true, ct);

    public async Task ClearAsync()
    {
        accessToken = null;
        await store.SetRefreshTokenAsync(null);
    }

    public void Dispose() => gate.Dispose();

    private async Task<bool> RefreshAsync(string? failedAccessToken, bool raiseExpired, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var current = accessToken;
            if (current is not null && current != failedAccessToken)
            {
                return true;
            }

            var refreshToken = await store.GetRefreshTokenAsync();
            var response = refreshToken is null ? null : await CallRefreshAsync(refreshToken, ct);
            if (response is not null)
            {
                await StoreAsync(response);
                return true;
            }

            await ClearAsync();
            if (raiseExpired && refreshToken is not null)
            {
                logger.LogInformation("Refresh token rejected; session expired");
                SessionExpired?.Invoke(this, EventArgs.Empty);
            }

            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <returns>null when the server rejected the token (400/401); throws on network/server errors so a blip never signs the user out.</returns>
    private async Task<AuthResponse?> CallRefreshAsync(string refreshToken, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient(RefreshClientName);
        using var response = await client.PostAsJsonAsync(ApiRoutes.AuthRefresh, new RefreshTokenRequest(refreshToken), ExpoviceApiClient.Json, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ExpoviceApiClient.ToApiExceptionAsync(response, ct);
        }

        return await response.Content.ReadFromJsonAsync<AuthResponse>(ExpoviceApiClient.Json, ct);
    }

    private async Task StoreAsync(AuthResponse response)
    {
        accessToken = response.AccessToken;
        await store.SetRefreshTokenAsync(response.RefreshToken);
    }
}
