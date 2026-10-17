using System.Security.Claims;
using Expovice.Api.Infrastructure;
using Expovice.Api.Services;
using Expovice.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Expovice.Api.Endpoints;

public static class ProviderEndpoints
{
    private const int TopCount = 6;

    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Providers").RequireAuthorization();
        group.MapGet(ApiRoutes.Providers, SearchAsync);
        group.MapGet(ApiRoutes.ProvidersTop, TopAsync);
        group.MapGet(ApiRoutes.Provider, GetAsync);
        return app;
    }

    private static async Task<Results<Ok<ProviderSearchResult>, ValidationProblem>> SearchAsync(
        [FromQuery(Name = SearchParams.Category)] string? category,
        [FromQuery(Name = SearchParams.Latitude)] double lat,
        [FromQuery(Name = SearchParams.Longitude)] double lon,
        [FromQuery(Name = SearchParams.RadiusKm)] int? radiusKm,
        [FromQuery(Name = SearchParams.Sort)] ProviderSort? sort,
        [FromQuery(Name = SearchParams.AvailableToday)] bool? availableToday,
        [FromQuery(Name = SearchParams.MaxRate)] int? maxRate,
        ClaimsPrincipal principal,
        ProviderSearch search,
        CancellationToken ct)
    {
        var radius = radiusKm ?? Limits.DefaultSearchRadiusKm;
        var v = ValidateLocation(lat, lon, radius)
            .Check(category is not null && Catalog.ById.ContainsKey(category), SearchParams.Category, "Unknown service category.");
        if (!v.IsValid)
        {
            return v.ToProblem();
        }

        var query = new ProviderQuery(lat, lon, radius, category, sort ?? ProviderSort.Distance, availableToday ?? false, maxRate, principal.UserId());
        return TypedResults.Ok(await search.SearchAsync(query, ct));
    }

    private static async Task<Results<Ok<IReadOnlyList<ProviderSummaryDto>>, ValidationProblem>> TopAsync(
        [FromQuery(Name = SearchParams.Latitude)] double lat,
        [FromQuery(Name = SearchParams.Longitude)] double lon,
        [FromQuery(Name = SearchParams.RadiusKm)] int? radiusKm,
        ClaimsPrincipal principal,
        ProviderSearch search,
        CancellationToken ct)
    {
        var radius = radiusKm ?? Limits.DefaultSearchRadiusKm;
        var v = ValidateLocation(lat, lon, radius);
        if (!v.IsValid)
        {
            return v.ToProblem();
        }

        var query = new ProviderQuery(lat, lon, radius, ExcludeUserId: principal.UserId());
        return TypedResults.Ok(await search.TopAsync(query, TopCount, ct));
    }

    private static async Task<Results<Ok<ProviderDetailDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        [FromQuery(Name = SearchParams.Latitude)] double? lat,
        [FromQuery(Name = SearchParams.Longitude)] double? lon,
        ProviderSearch search,
        IPhotoStore photos,
        CancellationToken ct)
    {
        if (await search.FindAsync(id, lat, lon, ct) is not { } hit)
        {
            return Problems.NotFound("This pro is no longer listed.");
        }

        var p = hit.Profile;
        return TypedResults.Ok(new ProviderDetailDto(
            p.UserId,
            p.User.Name,
            await photos.GetReadUrlAsync(p.User.PhotoBlob, ct),
            p.CategoryIds,
            p.Skills,
            p.Rating,
            p.RatingCount,
            p.RequestCount,
            p.YearsOfExperience,
            Geo.RoundKm(hit.DistanceKm),
            p.Rate,
            p.Unit,
            search.IsAvailableToday(p),
            p.IsVerified,
            p.Bio,
            p.Languages,
            p.WorkDays,
            p.From,
            p.To,
            await MeEndpoints.WorkPhotoUrlsAsync(p, photos, ct)));
    }

    private static Validator ValidateLocation(double lat, double lon, int radiusKm) =>
        new Validator()
            .Check(lat is >= -90 and <= 90, SearchParams.Latitude, "Latitude is out of range.")
            .Check(lon is >= -180 and <= 180, SearchParams.Longitude, "Longitude is out of range.")
            .Check(radiusKm is >= Limits.RadiusMinKm and <= Limits.RadiusMaxKm, SearchParams.RadiusKm, $"Radius must be {Limits.RadiusMinKm}–{Limits.RadiusMaxKm} km.");
}
