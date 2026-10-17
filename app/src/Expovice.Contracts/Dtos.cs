namespace Expovice.Contracts;

// ---------- Auth ----------

/// <param name="PhoneNumber">E.164 format, e.g. +919876543210.</param>
public sealed record SendOtpRequest(string PhoneNumber);

public sealed record SendOtpResponse(int ResendAfterSeconds, int CodeLength);

public sealed record VerifyOtpRequest(string PhoneNumber, string Code);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    bool IsNewUser);

// ---------- Me ----------

public sealed record HomeAddressDto(string Label, string Line, double Latitude, double Longitude);

public sealed record MeDto(
    Guid Id,
    string Name,
    string? PhoneNumber,
    string? PhotoUrl,
    HomeAddressDto? Home,
    bool IsProvider);

public sealed record UpdateNameRequest(string Name);

public sealed record PhotoUploadResponse(PhotoSlot Slot, string? Url);

// ---------- Providers (customer side) ----------

public sealed record ProviderSummaryDto(
    Guid Id,
    string Name,
    string? PhotoUrl,
    string CategoryId,
    IReadOnlyList<string> Skills,
    double Rating,
    int RatingCount,
    int RequestCount,
    double DistanceKm,
    int Rate,
    RateUnit Unit,
    bool AvailableToday,
    bool IsVerified);

public sealed record ProviderSearchResult(IReadOnlyList<ProviderSummaryDto> Items, int BeyondRadiusCount);

public sealed record ProviderDetailDto(
    Guid Id,
    string Name,
    string? PhotoUrl,
    IReadOnlyList<string> CategoryIds,
    IReadOnlyList<string> Skills,
    double Rating,
    int RatingCount,
    int RequestCount,
    int YearsOfExperience,
    double DistanceKm,
    int Rate,
    RateUnit Unit,
    bool AvailableToday,
    bool IsVerified,
    string Bio,
    IReadOnlyList<string> Languages,
    IReadOnlyList<DayOfWeek> WorkDays,
    TimeOnly From,
    TimeOnly To,
    IReadOnlyList<string> WorkPhotoUrls);

// ---------- Provider profile (pro side) ----------

public sealed record UpsertProviderProfileRequest(
    string Name,
    string Bio,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> CategoryIds,
    IReadOnlyList<string> Skills,
    int YearsOfExperience,
    int Rate,
    RateUnit Unit,
    int RadiusKm,
    IReadOnlyList<DayOfWeek> WorkDays,
    TimeOnly From,
    TimeOnly To,
    IdDocumentType IdType);

public sealed record ProviderProfileDto(
    string Bio,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> CategoryIds,
    IReadOnlyList<string> Skills,
    int YearsOfExperience,
    int Rate,
    RateUnit Unit,
    int RadiusKm,
    IReadOnlyList<DayOfWeek> WorkDays,
    TimeOnly From,
    TimeOnly To,
    IdDocumentType IdType,
    bool HasIdDocument,
    bool IsVerified,
    bool IsAccepting,
    IReadOnlyList<string> WorkPhotoUrls);

public sealed record SetAcceptingRequest(bool IsAccepting);

// ---------- Service requests ----------

public sealed record CreateServiceRequest(
    Guid ProviderId,
    string CategoryId,
    string Skill,
    string? Note,
    DateOnly Date,
    string Slot);

public sealed record ServiceRequestDto(
    Guid Id,
    Guid ProviderId,
    string ProviderName,
    string? ProviderPhotoUrl,
    string CategoryId,
    string Skill,
    string? Note,
    DateOnly Date,
    string Slot,
    RequestStatus Status,
    int? MyRating,
    int Rate,
    RateUnit Unit);

public sealed record RateServiceRequest(int Stars);

public sealed record IncomingRequestDto(
    Guid Id,
    string CustomerName,
    double DistanceKm,
    string Area,
    string Skill,
    string? Note,
    DateOnly Date,
    string Slot,
    RequestStatus Status);

public sealed record WorkSummaryDto(int NewCount, int AcceptedCount, int BookedValue);
