using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Expovice.Api.Data;
using Expovice.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Expovice.Api.Tests;

public sealed class ProfileTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    private static MultipartFormDataContent Photo(byte[] bytes, string contentType = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", "photo.png" } };
    }

    [Fact]
    public async Task UpsertProvider_WithoutHome_Returns400()
    {
        var (client, _) = await api.SignInAsync();
        var res = await client.PutAsJsonAsync(ApiRoutes.MeProvider, ApiFactory.Profile(), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task UpsertProvider_SkillOutsideChosenCategory_Returns400()
    {
        var (client, _) = await api.SignInAsync();
        await ApiFactory.SetHomeAsync(client, 12.97, 77.64);
        var res = await client.PutAsJsonAsync(ApiRoutes.MeProvider, ApiFactory.Profile() with { Skills = ["Gas refill"] }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(Ct);
        Assert.Contains(nameof(UpsertProviderProfileRequest.Skills), problem!.Errors.Keys);
    }

    [Fact]
    public async Task UpsertProvider_ThenRead_AndMeShowsProvider()
    {
        var (client, _) = await api.SignInAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ApiRoutes.MeProvider, Ct)).StatusCode);
        await ApiFactory.SetHomeAsync(client, 12.97, 77.64);

        var put = await client.PutAsJsonAsync(ApiRoutes.MeProvider, ApiFactory.Profile(name: "Lakshmi Devi"), Ct);
        put.EnsureSuccessStatusCode();

        var profile = (await client.GetFromJsonAsync<ProviderProfileDto>(ApiRoutes.MeProvider, Ct))!;
        Assert.Equal(["electrician"], profile.CategoryIds);
        Assert.True(profile.IsAccepting);
        Assert.False(profile.HasIdDocument);

        var me = (await client.GetFromJsonAsync<MeDto>(ApiRoutes.Me, Ct))!;
        Assert.True(me.IsProvider);
        Assert.Equal("Lakshmi Devi", me.Name);
    }

    [Fact]
    public async Task PhotoUpload_ValidatesAndStores()
    {
        var (client, _) = await api.SignInAsync();

        var notImage = await client.PostAsync(ApiRoutes.MePhotoFor(PhotoSlot.Profile), Photo("hello world!"u8.ToArray()), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);

        var workBeforeListing = await client.PostAsync(ApiRoutes.MePhotoFor(PhotoSlot.Work1), Photo(PngHeader), Ct);
        Assert.Equal(HttpStatusCode.Conflict, workBeforeListing.StatusCode);

        var ok = await client.PostAsync(ApiRoutes.MePhotoFor(PhotoSlot.Profile), Photo(PngHeader), Ct);
        ok.EnsureSuccessStatusCode();
        var body = (await ok.Content.ReadFromJsonAsync<PhotoUploadResponse>(Ct))!;
        Assert.StartsWith("https://photos.test/", body.Url);

        var me = (await client.GetFromJsonAsync<MeDto>(ApiRoutes.Me, Ct))!;
        Assert.Equal(body.Url, me.PhotoUrl);
    }

    [Fact]
    public async Task IdDocument_IsStoredButNeverExposed()
    {
        var (client, _) = await api.CreateProviderAsync(12.97, 77.64);
        var res = await client.PostAsync(ApiRoutes.MePhotoFor(PhotoSlot.IdFront), Photo(PngHeader), Ct);
        res.EnsureSuccessStatusCode();
        Assert.Null((await res.Content.ReadFromJsonAsync<PhotoUploadResponse>(Ct))!.Url);
        Assert.True((await client.GetFromJsonAsync<ProviderProfileDto>(ApiRoutes.MeProvider, Ct))!.HasIdDocument);
    }

    [Fact]
    public async Task SampleData_SeedsPrototypeProviders()
    {
        // Fresh database: the seeder only runs when no providers exist yet.
        await using var fresh = new ApiFactory();
        using var scope = fresh.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpoviceDbContext>();
        await SampleDataSeeder.SeedAsync(db, fresh.Time, NullLogger.Instance, Ct);

        // First electrician in the prototype: rnd-derived values for ci=0, k=0 (s=1).
        var ravi = db.Providers.Where(p => p.CategoryIds.Contains("electrician")).AsEnumerable()
            .Single(p => db.Users.Single(u => u.Id == p.UserId).Name == "Ravi Kumar" && p.Skills[0] == "Wiring");
        Assert.Equal(18 + (int)Math.Floor(SampleDataSeeder.Rnd(1) * 420), ravi.RequestCount);
        Assert.Equal(Math.Round(3.6 + SampleDataSeeder.Rnd(2) * 1.4, 1, MidpointRounding.AwayFromZero), ravi.Rating);
        Assert.Equal(Catalog.Categories.Count * 7, db.Providers.Count(p => p.User.PhoneNumber == null));
    }
}
