using Expovice.Api.Data;
using Expovice.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Expovice.Api.Services;

public sealed record ProviderQuery(
    double Latitude,
    double Longitude,
    int RadiusKm,
    string? CategoryId = null,
    ProviderSort Sort = ProviderSort.Distance,
    bool AvailableTodayOnly = false,
    int? MaxRate = null,
    Guid? ExcludeUserId = null);

/// <summary>A provider row joined with its computed distance to the customer.</summary>
public sealed record ProviderHit(ProviderProfile Profile, double DistanceKm);

/// <summary>
/// "Pros near me" search. SQL narrows candidates with an indexed lat/lon bounding box,
/// then exact Haversine distance is applied in memory. Works on SQL Server and SQLite alike.
/// </summary>
/// <remarks>Scoped (uses DbContext) — not thread-safe.</remarks>
public sealed class ProviderSearch(ExpoviceDbContext db, IPhotoStore photos, TimeProvider time)
{
    /// <summary>"Beyond radius" counts pros between the radius and this multiple of it.</summary>
    private const int BeyondFactor = 2;

    public async Task<ProviderSearchResult> SearchAsync(ProviderQuery q, CancellationToken ct)
    {
        // Fetch a ring twice the radius in one query: the inner circle is the result,
        // the rest feeds the "N more beyond X km aren't shown" hint.
        var outerKm = q.RadiusKm * BeyondFactor;
        var candidates = await CandidatesAsync(q, outerKm, ct);
        var hits = InRange(candidates, q);
        var beyond = candidates.Count(p => Geo.DistanceKm(q.Latitude, q.Longitude, p.Latitude, p.Longitude) <= outerKm) - hits.Count;

        var today = IndiaClock.Now(time).DayOfWeek;
        var filtered = hits
            .Where(h => !q.AvailableTodayOnly || IsAvailable(h.Profile, today))
            .Where(h => q.MaxRate is not { } max || h.Profile.Rate < max);

        var items = await ToSummariesAsync(Sort(filtered, q.Sort), q.CategoryId, ct);
        return new ProviderSearchResult(items, beyond);
    }

    public async Task<IReadOnlyList<ProviderSummaryDto>> TopAsync(ProviderQuery q, int take, CancellationToken ct)
    {
        var hits = InRange(await CandidatesAsync(q, q.RadiusKm, ct), q);
        return await ToSummariesAsync(Sort(hits, ProviderSort.Rating).Take(take), null, ct);
    }

    public async Task<ProviderHit?> FindAsync(Guid id, double? lat, double? lon, CancellationToken ct)
    {
        var p = await db.Providers.Include(x => x.User).AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id, ct);
        return p is null ? null : new ProviderHit(p, lat is { } la && lon is { } lo ? Geo.DistanceKm(la, lo, p.Latitude, p.Longitude) : 0);
    }

    public static bool IsAvailable(ProviderProfile p, DayOfWeek today) => p.IsAccepting && p.WorkDays.Contains(today);

    public bool IsAvailableToday(ProviderProfile p) => IsAvailable(p, IndiaClock.Now(time).DayOfWeek);

    private IQueryable<ProviderProfile> Accepting(ProviderQuery q)
    {
        var query = db.Providers.AsNoTracking().Where(p => p.IsAccepting);
        if (q.CategoryId is { } cat)
        {
            query = query.Where(p => p.CategoryIds.Contains(cat));
        }
        if (q.ExcludeUserId is { } me)
        {
            query = query.Where(p => p.UserId != me);
        }
        return query;
    }

    private Task<List<ProviderProfile>> CandidatesAsync(ProviderQuery q, double boxKm, CancellationToken ct)
    {
        var box = Geo.BoxAround(q.Latitude, q.Longitude, boxKm);
        return Accepting(q)
            .Include(p => p.User)
            .Where(p => p.Latitude >= box.MinLat && p.Latitude <= box.MaxLat && p.Longitude >= box.MinLon && p.Longitude <= box.MaxLon)
            .ToListAsync(ct);
    }

    /// <summary>Keeps pros the customer is within reach of: inside both the search radius and the pro's own service radius.</summary>
    private static List<ProviderHit> InRange(List<ProviderProfile> candidates, ProviderQuery q) =>
        candidates
            .Select(p => new ProviderHit(p, Geo.DistanceKm(q.Latitude, q.Longitude, p.Latitude, p.Longitude)))
            .Where(h => h.DistanceKm <= Math.Min(q.RadiusKm, h.Profile.RadiusKm))
            .ToList();

    private static IEnumerable<ProviderHit> Sort(IEnumerable<ProviderHit> hits, ProviderSort sort) =>
        sort == ProviderSort.Rating
            ? hits.OrderByDescending(h => h.Profile.Rating).ThenBy(h => h.DistanceKm)
            : hits.OrderBy(h => h.DistanceKm);

    private async Task<IReadOnlyList<ProviderSummaryDto>> ToSummariesAsync(IEnumerable<ProviderHit> hits, string? categoryId, CancellationToken ct)
    {
        var today = IndiaClock.Now(time).DayOfWeek;
        var list = new List<ProviderSummaryDto>();
        foreach (var (p, km) in hits)
        {
            var cat = categoryId ?? p.CategoryIds.FirstOrDefault() ?? "";
            var catSkills = Catalog.ById.TryGetValue(cat, out var c) ? c.Skills : [];
            list.Add(new ProviderSummaryDto(
                p.UserId,
                p.User.Name,
                await photos.GetReadUrlAsync(p.User.PhotoBlob, ct),
                cat,
                p.Skills.Where(catSkills.Contains).Take(3).ToList(),
                p.Rating,
                p.RatingCount,
                p.RequestCount,
                Geo.RoundKm(km),
                p.Rate,
                p.Unit,
                IsAvailable(p, today),
                p.IsVerified));
        }
        return list;
    }
}
