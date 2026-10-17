using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Expovice.App.Core.Abstractions;
using Expovice.Contracts;
using Microsoft.Extensions.Logging;

namespace Expovice.App.Core.Api;

/// <summary>
/// Typed HttpClient for the API. Thread-safe: it holds no mutable state beyond the HttpClient,
/// which is itself safe for concurrent requests. Bearer tokens are attached by <see cref="AuthTokenHandler"/>.
/// </summary>
public sealed class ExpoviceApiClient(HttpClient http, ILogger<ExpoviceApiClient> logger) : IExpoviceApi
{
    /// <summary>camelCase, case-insensitive, string enums (via the attributes on the Contracts enums).</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<SendOtpResponse> SendOtpAsync(string phoneNumber, CancellationToken ct = default) =>
        SendAsync<SendOtpResponse>(HttpMethod.Post, ApiRoutes.AuthOtpSend, new SendOtpRequest(phoneNumber), ct);

    public Task<AuthResponse> VerifyOtpAsync(string phoneNumber, string code, CancellationToken ct = default) =>
        SendAsync<AuthResponse>(HttpMethod.Post, ApiRoutes.AuthOtpVerify, new VerifyOtpRequest(phoneNumber, code), ct);

    public Task SignOutAsync(string refreshToken, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ApiRoutes.AuthSignOut, new RefreshTokenRequest(refreshToken), ct);

    public Task<MeDto> GetMeAsync(CancellationToken ct = default) =>
        SendAsync<MeDto>(HttpMethod.Get, ApiRoutes.Me, null, ct);

    public Task UpdateNameAsync(string name, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, ApiRoutes.MeName, new UpdateNameRequest(name), ct);

    public Task SetHomeAsync(HomeAddressDto home, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, ApiRoutes.MeHome, home, ct);

    public async Task<PhotoUploadResponse> UploadPhotoAsync(PhotoSlot slot, PickedPhoto photo, CancellationToken ct = default)
    {
        // ByteArrayContent (not StreamContent) so AuthTokenHandler can resend it after a token refresh.
        var file = new ByteArrayContent(photo.Bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(photo.ContentType);
        var form = new MultipartFormDataContent { { file, "file", photo.FileName } };
        return await SendAsync<PhotoUploadResponse?>(HttpMethod.Post, ApiRoutes.MePhotoFor(slot), form, ct)
            ?? new PhotoUploadResponse(slot, null);
    }

    public Task<ProviderSearchResult> SearchProvidersAsync(ProviderQuery query, CancellationToken ct = default)
    {
        var q = query;
        var parameters = new Dictionary<string, string?>
        {
            [SearchParams.Category] = q.CategoryId,
            [SearchParams.Latitude] = Num(q.Latitude),
            [SearchParams.Longitude] = Num(q.Longitude),
            [SearchParams.RadiusKm] = q.RadiusKm.ToString(CultureInfo.InvariantCulture),
            [SearchParams.Sort] = q.Sort.ToString(),
            [SearchParams.AvailableToday] = q.AvailableToday ? "true" : null,
            [SearchParams.MaxRate] = q.MaxRate?.ToString(CultureInfo.InvariantCulture),
        };
        return SendAsync<ProviderSearchResult>(HttpMethod.Get, WithQuery(ApiRoutes.Providers, parameters), null, ct);
    }

    public async Task<IReadOnlyList<ProviderSummaryDto>> GetTopProvidersAsync(double latitude, double longitude, int radiusKm, CancellationToken ct = default)
    {
        var url = WithQuery(ApiRoutes.ProvidersTop, LocationQuery(latitude, longitude, radiusKm));
        // Accept either a bare array or a ProviderSearchResult envelope.
        using var doc = await SendAsync<JsonDocument>(HttpMethod.Get, url, null, ct);
        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
        return items.Deserialize<List<ProviderSummaryDto>>(Json) ?? [];
    }

    public Task<ProviderDetailDto> GetProviderAsync(Guid id, double latitude, double longitude, CancellationToken ct = default) =>
        SendAsync<ProviderDetailDto>(HttpMethod.Get, WithQuery(ApiRoutes.ProviderFor(id), LocationQuery(latitude, longitude, null)), null, ct);

    public async Task<ProviderProfileDto?> GetMyProviderProfileAsync(CancellationToken ct = default)
    {
        try
        {
            return await SendAsync<ProviderProfileDto>(HttpMethod.Get, ApiRoutes.MeProvider, null, ct);
        }
        catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task UpsertProviderProfileAsync(UpsertProviderProfileRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, ApiRoutes.MeProvider, request, ct);

    public Task SetAcceptingAsync(bool isAccepting, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, ApiRoutes.MeProviderAccepting, new SetAcceptingRequest(isAccepting), ct);

    public Task CreateRequestAsync(CreateServiceRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ApiRoutes.Requests, request, ct);

    public async Task<IReadOnlyList<ServiceRequestDto>> GetMyRequestsAsync(CancellationToken ct = default) =>
        await SendAsync<List<ServiceRequestDto>>(HttpMethod.Get, ApiRoutes.Requests, null, ct);

    public Task CancelRequestAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ApiRoutes.RequestCancelFor(id), null, ct);

    public Task RateRequestAsync(Guid id, int stars, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ApiRoutes.RequestRateFor(id), new RateServiceRequest(stars), ct);

    public Task<WorkSummaryDto> GetWorkSummaryAsync(CancellationToken ct = default) =>
        SendAsync<WorkSummaryDto>(HttpMethod.Get, ApiRoutes.WorkSummary, null, ct);

    public async Task<IReadOnlyList<IncomingRequestDto>> GetIncomingRequestsAsync(CancellationToken ct = default) =>
        await SendAsync<List<IncomingRequestDto>>(HttpMethod.Get, ApiRoutes.WorkRequests, null, ct);

    public Task WorkActionAsync(Guid id, WorkAction action, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, ApiRoutes.WorkActionFor(id, action.ToString().ToLowerInvariant()), null, ct);

    // ---------- plumbing (DRY: every call goes through these two methods) ----------

    private async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, url, body, ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, url, body, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default!;
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = body switch
            {
                null => null,
                HttpContent content => content,
                _ => JsonContent.Create(body, body.GetType(), options: Json),
            },
        };

        var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var error = await ToApiExceptionAsync(response, ct);
            logger.LogWarning("{Method} {Url} failed: {Status} {Title}", method, url, (int)response.StatusCode, error.Title);
            throw error;
        }
    }

    internal static async Task<ApiException> ToApiExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ProblemPayload? problem = null;
        try
        {
            if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            {
                problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(Json, ct);
            }
        }
        catch (JsonException)
        {
            // Not ProblemDetails; fall back to the status code text.
        }

        var title = problem?.Title ?? response.ReasonPhrase ?? response.StatusCode.ToString();
        return new ApiException(response.StatusCode, title, problem?.Detail, problem?.Errors);
    }

    private static Dictionary<string, string?> LocationQuery(double lat, double lon, int? radiusKm) => new()
    {
        [SearchParams.Latitude] = Num(lat),
        [SearchParams.Longitude] = Num(lon),
        [SearchParams.RadiusKm] = radiusKm?.ToString(CultureInfo.InvariantCulture),
    };

    private static string Num(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    internal static string WithQuery(string path, IEnumerable<KeyValuePair<string, string?>> query)
    {
        var parts = query
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}");
        var qs = string.Join('&', parts);
        return qs.Length == 0 ? path : $"{path}?{qs}";
    }
}
