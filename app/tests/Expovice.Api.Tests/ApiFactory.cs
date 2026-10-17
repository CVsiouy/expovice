using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Expovice.Api.Data;
using Expovice.Api.Services;
using Expovice.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Expovice.Api.Tests;

/// <summary>
/// Hosts the real API in memory with SQLite, an in-memory photo store, a capturing SMS sender and a fake clock.
/// One instance per test class (IClassFixture), so each class gets its own database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Saturday 17 Oct 2026, 12:00 IST.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 17, 6, 30, 0, TimeSpan.Zero);

    // A temp-file database (not :memory:) so concurrent requests each get their own connection,
    // like production; Default Timeout makes writers wait on SQLite's lock instead of failing.
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"expovice-tests-{Guid.NewGuid():N}.db");
    private int _phoneCounter;

    public FakeTimeProvider Time { get; } = new(Start);
    public CapturingSmsSender Sms { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Sql", "Server=unused");
        builder.UseSetting("Storage:ConnectionString", "UseDevelopmentStorage=true");
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[48]));
        builder.UseSetting("RateLimiting:OtpPermitsPerMinute", "10000");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ExpoviceDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ExpoviceDbContext>>();
            services.AddDbContext<ExpoviceDbContext>(o => o.UseSqlite($"Data Source={_dbPath};Default Timeout=30;Pooling=False"));
            services.RemoveAll<IPhotoStore>();
            services.AddSingleton<IPhotoStore, InMemoryPhotoStore>();
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(Sms);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ExpoviceDbContext>().Database.EnsureCreated();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            File.Delete(_dbPath);
        }
    }

    public string NewPhone() => $"+9198{Interlocked.Increment(ref _phoneCounter):D8}";

    public T WithDb<T>(Func<ExpoviceDbContext, T> query)
    {
        using var scope = Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ExpoviceDbContext>());
    }

    /// <summary>Signs a new user in through the real OTP flow and returns an authorised client.</summary>
    public async Task<(HttpClient Client, AuthResponse Auth)> SignInAsync(string? phone = null, string? name = "Test User")
    {
        phone ??= NewPhone();
        var client = CreateClient();
        (await client.PostAsJsonAsync(ApiRoutes.AuthOtpSend, new SendOtpRequest(phone))).EnsureSuccessStatusCode();
        var verify = await client.PostAsJsonAsync(ApiRoutes.AuthOtpVerify, new VerifyOtpRequest(phone, Sms.LastCodeFor(phone)));
        verify.EnsureSuccessStatusCode();
        var auth = (await verify.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        if (name is not null)
        {
            (await client.PutAsJsonAsync(ApiRoutes.MeName, new UpdateNameRequest(name))).EnsureSuccessStatusCode();
        }
        return (client, auth);
    }

    public static async Task SetHomeAsync(HttpClient client, double lat, double lon, string line = "12th Main, Indiranagar") =>
        (await client.PutAsJsonAsync(ApiRoutes.MeHome, new HomeAddressDto("Home", line, lat, lon))).EnsureSuccessStatusCode();

    public static UpsertProviderProfileRequest Profile(
        string category = "electrician", int rate = 300, int radiusKm = 10, IReadOnlyList<DayOfWeek>? days = null, string name = "Pro Person") =>
        new(
            name,
            "Experienced and tidy.",
            ["English"],
            [category],
            Catalog.ById[category].Skills.Take(3).ToList(),
            5,
            rate,
            RateUnit.Visit,
            radiusKm,
            days ?? [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday],
            new TimeOnly(9, 0),
            new TimeOnly(19, 0),
            IdDocumentType.Aadhaar);

    /// <summary>Signs in a new user, places them at (lat, lon) and lists them as a pro.</summary>
    public async Task<(HttpClient Client, Guid Id)> CreateProviderAsync(double lat, double lon, UpsertProviderProfileRequest? profile = null)
    {
        var (client, _) = await SignInAsync();
        await SetHomeAsync(client, lat, lon);
        (await client.PutAsJsonAsync(ApiRoutes.MeProvider, profile ?? Profile())).EnsureSuccessStatusCode();
        var me = (await client.GetFromJsonAsync<MeDto>(ApiRoutes.Me))!;
        return (client, me.Id);
    }
}

public sealed class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentDictionary<string, string> _last = new();

    public Task SendAsync(string phoneNumber, string message, CancellationToken ct)
    {
        _last[phoneNumber] = message;
        return Task.CompletedTask;
    }

    /// <summary>The OTP is the first word of the message.</summary>
    public string LastCodeFor(string phone) => _last[phone].Split(' ')[0];
}

/// <summary>Thread-safe in-memory photo store for tests.</summary>
public sealed class InMemoryPhotoStore : IPhotoStore
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    public async Task<string> SaveAsync(Guid userId, PhotoSlot slot, Stream content, string contentType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var name = PhotoTypes.BlobName(userId, slot, contentType);
        _blobs[name] = ms.ToArray();
        return name;
    }

    public Task DeleteAsync(PhotoSlot slot, string blobName, CancellationToken ct)
    {
        _blobs.TryRemove(blobName, out _);
        return Task.CompletedTask;
    }

    public Task<string?> GetReadUrlAsync(string? blobName, CancellationToken ct) =>
        Task.FromResult(blobName is null ? null : $"https://photos.test/{blobName}?sas=1");

    public bool Contains(string blobName) => _blobs.ContainsKey(blobName);
}
