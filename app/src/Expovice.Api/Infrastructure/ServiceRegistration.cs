using System.Threading.RateLimiting;
using Expovice.Api.Data;
using Expovice.Api.Endpoints;
using Expovice.Api.Options;
using Expovice.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Expovice.Api.Infrastructure;

public static class ServiceRegistration
{
    /// <summary>
    /// Dev-only fallback so `dotnet run` works before user-secrets are set. Never used outside Development.
    /// </summary>
    private const string DevOnlySigningKey = "ZGV2LW9ubHktZXhwb3ZpY2Utc2lnbmluZy1rZXktZG8tbm90LXVzZQ==";

    private static readonly string[] AppleSettings = ["ClientId", "TeamId", "KeyId", "PrivateKey"];

    public static IServiceCollection AddExpoviceData(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<DatabaseOptions>().Bind(config.GetSection(DatabaseOptions.Section));
        services.AddDbContext<ExpoviceDbContext>(o => o.UseSqlServer(
            config.GetConnectionString("Sql") ?? throw new InvalidOperationException("ConnectionStrings:Sql is not configured."),
            sql => sql.EnableRetryOnFailure()));
        services.AddHealthChecks().AddDbContextCheck<ExpoviceDbContext>();
        return services;
    }

    public static IServiceCollection AddExpoviceAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddOptions<JwtOptions>()
            .Bind(config.GetSection(JwtOptions.Section))
            .PostConfigure(o =>
            {
                if (string.IsNullOrEmpty(o.SigningKey) && env.IsDevelopment())
                {
                    o.SigningKey = DevOnlySigningKey;
                }
            })
            .ValidateDataAnnotations()
            .Validate(o => IsValidKey(o.SigningKey), "Jwt:SigningKey must be base64 and at least 32 bytes.")
            .ValidateOnStart();

        services.AddScoped<TokenService>();
        services.AddScoped<OtpService>();
        services.AddScoped<AccountService>();

        var auth = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddCookie(AuthEndpoints.ExternalCookieScheme, o =>
            {
                o.Cookie.SameSite = SameSiteMode.None;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            });

        // Bearer validation reads the same validated JwtOptions (incl. the Key Vault-sourced key).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((o, jwt, time) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = TokenService.CreateSigningKey(jwt.Value.SigningKey),
                    NameClaimType = "name",
                    ClockSkew = TimeSpan.FromMinutes(1),
                    // Validate expiry against the injected clock (testable) instead of DateTime.UtcNow.
                    LifetimeValidator = (notBefore, expires, _, p) =>
                    {
                        var now = time.GetUtcNow().UtcDateTime;
                        return (notBefore is null || notBefore <= now + p.ClockSkew) && expires is not null && expires > now - p.ClockSkew;
                    },
                };
            });

        var google = config.GetSection("Authentication:Google");
        if (!string.IsNullOrEmpty(google["ClientId"]) && !string.IsNullOrEmpty(google["ClientSecret"]))
        {
            auth.AddGoogle(AuthEndpoints.SchemeFor(Contracts.ExternalProvider.Google), o =>
            {
                o.ClientId = google["ClientId"]!;
                o.ClientSecret = google["ClientSecret"]!;
                o.SignInScheme = AuthEndpoints.ExternalCookieScheme;
            });
        }

        var apple = config.GetSection("Authentication:Apple");
        if (AppleSettings.All(k => !string.IsNullOrEmpty(apple[k])))
        {
            auth.AddApple(AuthEndpoints.SchemeFor(Contracts.ExternalProvider.Apple), o =>
            {
                o.ClientId = apple["ClientId"]!;
                o.TeamId = apple["TeamId"]!;
                o.KeyId = apple["KeyId"]!;
                o.GenerateClientSecret = true;
                // The .p8 key content (PEM) comes from Key Vault; never stored in source.
                o.PrivateKey = (keyId, _) => Task.FromResult(apple["PrivateKey"]!.AsMemory());
                o.SignInScheme = AuthEndpoints.ExternalCookieScheme;
            });
        }

        services.AddAuthorization();
        var otpPermitsPerMinute = config.GetValue("RateLimiting:OtpPermitsPerMinute", 10);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthEndpoints.OtpRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = otpPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    public static IServiceCollection AddExpoviceMessaging(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<SmsOptions>().Bind(config.GetSection(SmsOptions.Section));
        if (!string.IsNullOrWhiteSpace(config[$"{SmsOptions.Section}:ConnectionString"]))
        {
            services.AddSingleton<ISmsSender, AzureCommunicationSmsSender>();
        }
        else
        {
            services.AddSingleton<ISmsSender, LoggingSmsSender>();
        }
        return services;
    }

    public static IServiceCollection AddExpoviceStorage(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<StorageOptions>()
            .Bind(config.GetSection(StorageOptions.Section))
            .Validate(o => o.BlobServiceUri is not null || !string.IsNullOrWhiteSpace(o.ConnectionString),
                "Storage:BlobServiceUri or Storage:ConnectionString is required.")
            .ValidateOnStart();
        services.AddSingleton<IPhotoStore, BlobPhotoStore>();
        services.AddScoped<ProviderSearch>();
        return services;
    }

    /// <summary>Applies migrations and seeds sample data when enabled in configuration.</summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!options.MigrateOnStartup && !options.SeedSampleData)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpoviceDbContext>();
        if (options.MigrateOnStartup)
        {
            await db.Database.MigrateAsync();
        }
        if (options.SeedSampleData)
        {
            await SampleDataSeeder.SeedAsync(db, app.Services.GetRequiredService<TimeProvider>(), app.Logger, CancellationToken.None);
        }
    }

    private static bool IsValidKey(string key)
    {
        Span<byte> buffer = stackalloc byte[512];
        return Convert.TryFromBase64String(key, buffer, out var written) && written >= 32;
    }
}
