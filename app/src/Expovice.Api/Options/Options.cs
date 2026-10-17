using System.ComponentModel.DataAnnotations;

namespace Expovice.Api.Options;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required]
    public string Issuer { get; set; } = "expovice-api";

    [Required]
    public string Audience { get; set; } = "expovice-app";

    /// <summary>Base64, at least 32 bytes. Comes from Key Vault in Azure, user-secrets locally.</summary>
    [Required]
    public string SigningKey { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 60;
}

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>e.g. https://stexpovice.blob.core.windows.net — used with managed identity.</summary>
    public Uri? BlobServiceUri { get; set; }

    /// <summary>Local development (Azurite). Takes precedence over <see cref="BlobServiceUri"/>.</summary>
    public string? ConnectionString { get; set; }

    public string PhotosContainer { get; set; } = "photos";
    public string IdDocumentsContainer { get; set; } = "id-documents";
    public int ReadUrlMinutes { get; set; } = 60;
}

public sealed class SmsOptions
{
    public const string Section = "Sms";

    /// <summary>Azure Communication Services connection string. Empty → codes are only logged (dev).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Sender number or alphanumeric sender ID registered with ACS.</summary>
    public string? From { get; set; }
}

public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public bool MigrateOnStartup { get; set; }
    public bool SeedSampleData { get; set; }
}
