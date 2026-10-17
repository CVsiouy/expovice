using System.Net;
using System.Net.Http.Json;
using Expovice.Api.Services;
using Expovice.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Expovice.Api.Tests;

/// <summary>Each test works around its own far-away base point so tests sharing the database don't see each other.</summary>
public sealed class ProviderSearchTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static string SearchUrl(double lat, double lon, string category = "electrician", string extra = "") =>
        $"{ApiRoutes.Providers}?{SearchParams.Category}={category}&{SearchParams.Latitude}={lat}&{SearchParams.Longitude}={lon}&{SearchParams.RadiusKm}=5{extra}";

    private async Task<HttpClient> CustomerAtAsync(double lat, double lon)
    {
        var (client, _) = await api.SignInAsync();
        await ApiFactory.SetHomeAsync(client, lat, lon);
        return client;
    }

    private Task<(HttpClient Client, Guid Id)> ProviderAtAsync(double baseLat, double baseLon, double km, UpsertProviderProfileRequest? profile = null)
    {
        var (lat, lon) = Geo.Offset(baseLat, baseLon, km, 45);
        return api.CreateProviderAsync(lat, lon, profile);
    }

    [Fact]
    public async Task Search_OnlyReturnsProsWithinRadius_AndCountsTheRest()
    {
        const double lat = 20.0, lon = 78.0;
        var (_, near) = await ProviderAtAsync(lat, lon, 1.5);
        var (_, far) = await ProviderAtAsync(lat, lon, 7);
        await ProviderAtAsync(lat, lon, 1, ApiFactory.Profile(category: "plumber"));
        var customer = await CustomerAtAsync(lat, lon);

        var result = (await customer.GetFromJsonAsync<ProviderSearchResult>(SearchUrl(lat, lon), Ct))!;

        var item = Assert.Single(result.Items);
        Assert.Equal(near, item.Id);
        Assert.Equal(1.5, item.DistanceKm, 1);
        Assert.Equal("electrician", item.CategoryId);
        Assert.Equal(3, item.Skills.Count);
        Assert.Equal(1, result.BeyondRadiusCount);
        Assert.DoesNotContain(result.Items, i => i.Id == far);
    }

    [Fact]
    public async Task Search_RespectsProvidersOwnServiceRadius()
    {
        const double lat = 21.0, lon = 78.0;
        await ProviderAtAsync(lat, lon, 3, ApiFactory.Profile(radiusKm: 2));
        var customer = await CustomerAtAsync(lat, lon);

        var result = (await customer.GetFromJsonAsync<ProviderSearchResult>(SearchUrl(lat, lon), Ct))!;
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Search_SortsByDistanceOrRating()
    {
        const double lat = 22.0, lon = 78.0;
        var (_, close) = await ProviderAtAsync(lat, lon, 1);
        var (_, wellRated) = await ProviderAtAsync(lat, lon, 3);
        api.WithDb(db => db.Providers.Where(p => p.UserId == wellRated)
            .ExecuteUpdate(s => s.SetProperty(p => p.RatingSum, 48).SetProperty(p => p.RatingCount, 10)));
        var customer = await CustomerAtAsync(lat, lon);

        var byDistance = (await customer.GetFromJsonAsync<ProviderSearchResult>(SearchUrl(lat, lon), Ct))!;
        Assert.Equal([close, wellRated], byDistance.Items.Select(i => i.Id));

        var byRating = (await customer.GetFromJsonAsync<ProviderSearchResult>(SearchUrl(lat, lon, extra: $"&{SearchParams.Sort}=Rating"), Ct))!;
        Assert.Equal([wellRated, close], byRating.Items.Select(i => i.Id));
        Assert.Equal(4.8, byRating.Items[0].Rating);
    }

    [Fact]
    public async Task Search_FiltersAvailableTodayAndMaxRate()
    {
        // The fake clock is a Saturday in IST.
        const double lat = 23.0, lon = 78.0;
        var weekdaysOnly = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        var (_, cheapToday) = await ProviderAtAsync(lat, lon, 1, ApiFactory.Profile(rate: 250));
        var (_, pricey) = await ProviderAtAsync(lat, lon, 2, ApiFactory.Profile(rate: 600));
        var (_, notToday) = await ProviderAtAsync(lat, lon, 3, ApiFactory.Profile(rate: 250, days: weekdaysOnly));
        var customer = await CustomerAtAsync(lat, lon);

        var today = (await customer.GetFromJsonAsync<ProviderSearchResult>(SearchUrl(lat, lon, extra: $"&{SearchParams.AvailableToday}=true"), Ct))!;
        Assert.Equal([cheapToday, pricey], today.Items.Select(i => i.Id));

        var cheap = (await customer.GetFromJsonAsync<ProviderSearchResult>(SearchUrl(lat, lon, extra: $"&{SearchParams.MaxRate}=400"), Ct))!;
        Assert.Equal([cheapToday, notToday], cheap.Items.Select(i => i.Id));
        Assert.False(cheap.Items[1].AvailableToday);
        Assert.DoesNotContain(cheap.Items, i => i.Id == pricey);
    }

    [Fact]
    public async Task Search_ExcludesYourself_AndPausedPros()
    {
        const double lat = 24.0, lon = 78.0;
        var (me, myId) = await ProviderAtAsync(lat, lon, 0.5);
        var (paused, _) = await ProviderAtAsync(lat, lon, 1);
        (await paused.PutAsJsonAsync(ApiRoutes.MeProviderAccepting, new SetAcceptingRequest(false), Ct)).EnsureSuccessStatusCode();
        var (_, other) = await ProviderAtAsync(lat, lon, 2);

        var result = (await me.GetFromJsonAsync<ProviderSearchResult>(SearchUrl(lat, lon), Ct))!;
        Assert.Equal([other], result.Items.Select(i => i.Id));
        Assert.DoesNotContain(result.Items, i => i.Id == myId);
    }

    [Fact]
    public async Task Top_ReturnsHighestRatedNearby()
    {
        const double lat = 25.0, lon = 78.0;
        var (_, a) = await ProviderAtAsync(lat, lon, 1);
        var (_, b) = await ProviderAtAsync(lat, lon, 2, ApiFactory.Profile(category: "dog"));
        api.WithDb(db => db.Providers.Where(p => p.UserId == b)
            .ExecuteUpdate(s => s.SetProperty(p => p.RatingSum, 50).SetProperty(p => p.RatingCount, 10)));
        var customer = await CustomerAtAsync(lat, lon);

        var top = (await customer.GetFromJsonAsync<List<ProviderSummaryDto>>(
            $"{ApiRoutes.ProvidersTop}?{SearchParams.Latitude}={lat}&{SearchParams.Longitude}={lon}", Ct))!;
        Assert.Equal([b, a], top.Select(p => p.Id));
        Assert.Equal("dog", top[0].CategoryId);
    }

    [Fact]
    public async Task Detail_ReturnsFullProfileWithDistance()
    {
        const double lat = 26.0, lon = 78.0;
        var (_, id) = await ProviderAtAsync(lat, lon, 2);
        var customer = await CustomerAtAsync(lat, lon);

        var detail = (await customer.GetFromJsonAsync<ProviderDetailDto>($"{ApiRoutes.ProviderFor(id)}?{SearchParams.Latitude}={lat}&{SearchParams.Longitude}={lon}", Ct))!;
        Assert.Equal("Pro Person", detail.Name);
        Assert.Equal(2, detail.DistanceKm, 1);
        Assert.Equal(["electrician"], detail.CategoryIds);
        Assert.True(detail.AvailableToday);

        var missing = await customer.GetAsync(ApiRoutes.ProviderFor(Guid.NewGuid()), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Search_UnknownCategory_Returns400()
    {
        var customer = await CustomerAtAsync(27, 78);
        var res = await customer.GetAsync(SearchUrl(27, 78, category: "astronaut"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
