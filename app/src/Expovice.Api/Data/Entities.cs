using Expovice.Contracts;

namespace Expovice.Api.Data;

public sealed class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string? PhoneNumber { get; set; }
    public ExternalProvider? ExternalProvider { get; set; }
    public string? ExternalSubject { get; set; }
    public string Name { get; set; } = "";
    public string? PhotoBlob { get; set; }
    public string? HomeLabel { get; set; }
    public string? HomeLine { get; set; }
    public double? HomeLat { get; set; }
    public double? HomeLon { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ProviderProfile? Provider { get; set; }

    public bool HasHome => HomeLat is not null && HomeLon is not null;
}

public sealed class ProviderProfile
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Bio { get; set; } = "";
    public List<string> Languages { get; set; } = [];
    public List<string> CategoryIds { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public int YearsOfExperience { get; set; }
    public int Rate { get; set; }
    public RateUnit Unit { get; set; }
    public int RadiusKm { get; set; }
    public List<DayOfWeek> WorkDays { get; set; } = [];
    public TimeOnly From { get; set; }
    public TimeOnly To { get; set; }
    public IdDocumentType IdType { get; set; }
    public string? IdDocumentBlob { get; set; }
    public bool IsVerified { get; set; }
    public bool IsAccepting { get; set; } = true;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int RequestCount { get; set; }
    public int RatingSum { get; set; }
    public int RatingCount { get; set; }
    public string? WorkPhoto1Blob { get; set; }
    public string? WorkPhoto2Blob { get; set; }
    public string? WorkPhoto3Blob { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public double Rating => RatingCount == 0 ? 0 : Math.Round((double)RatingSum / RatingCount, 1);

    public IEnumerable<string?> WorkPhotoBlobs => [WorkPhoto1Blob, WorkPhoto2Blob, WorkPhoto3Blob];
}

public sealed class ServiceRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CustomerId { get; set; }
    public User Customer { get; set; } = null!;
    public Guid ProviderId { get; set; }
    public ProviderProfile Provider { get; set; } = null!;
    public string CategoryId { get; set; } = "";
    public string Skill { get; set; } = "";
    public string? Note { get; set; }
    public DateOnly Date { get; set; }
    public string Slot { get; set; } = "";
    public RequestStatus Status { get; set; }
    public int? CustomerRating { get; set; }
    public string AddressLine { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Rate { get; set; }
    public RateUnit Unit { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OtpChallenge
{
    public string PhoneNumber { get; set; } = "";
    public byte[] CodeHash { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset LastSentAt { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
