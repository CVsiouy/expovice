using Expovice.App.Core.Abstractions;
using Expovice.Contracts;

namespace Expovice.App.Core.Api;

/// <summary>Search filters for a category listing.</summary>
public sealed record ProviderQuery(
    string CategoryId,
    double Latitude,
    double Longitude,
    int RadiusKm,
    ProviderSort Sort,
    bool AvailableToday,
    int? MaxRate);

public enum WorkAction
{
    Accept,
    Decline,
    Complete,
}

/// <summary>Typed client for the Expovice API. All methods throw <see cref="ApiException"/> on non-success responses.</summary>
public interface IExpoviceApi
{
    Task<SendOtpResponse> SendOtpAsync(string phoneNumber, CancellationToken ct = default);

    Task<AuthResponse> VerifyOtpAsync(string phoneNumber, string code, CancellationToken ct = default);

    Task SignOutAsync(string refreshToken, CancellationToken ct = default);

    Task<MeDto> GetMeAsync(CancellationToken ct = default);

    Task UpdateNameAsync(string name, CancellationToken ct = default);

    Task SetHomeAsync(HomeAddressDto home, CancellationToken ct = default);

    Task<PhotoUploadResponse> UploadPhotoAsync(PhotoSlot slot, PickedPhoto photo, CancellationToken ct = default);

    Task<ProviderSearchResult> SearchProvidersAsync(ProviderQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<ProviderSummaryDto>> GetTopProvidersAsync(double latitude, double longitude, int radiusKm, CancellationToken ct = default);

    Task<ProviderDetailDto> GetProviderAsync(Guid id, double latitude, double longitude, CancellationToken ct = default);

    /// <returns>null when the user has not listed themselves yet (404).</returns>
    Task<ProviderProfileDto?> GetMyProviderProfileAsync(CancellationToken ct = default);

    Task UpsertProviderProfileAsync(UpsertProviderProfileRequest request, CancellationToken ct = default);

    Task SetAcceptingAsync(bool isAccepting, CancellationToken ct = default);

    Task CreateRequestAsync(CreateServiceRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceRequestDto>> GetMyRequestsAsync(CancellationToken ct = default);

    Task CancelRequestAsync(Guid id, CancellationToken ct = default);

    Task RateRequestAsync(Guid id, int stars, CancellationToken ct = default);

    Task<WorkSummaryDto> GetWorkSummaryAsync(CancellationToken ct = default);

    Task<IReadOnlyList<IncomingRequestDto>> GetIncomingRequestsAsync(CancellationToken ct = default);

    Task WorkActionAsync(Guid id, WorkAction action, CancellationToken ct = default);
}
