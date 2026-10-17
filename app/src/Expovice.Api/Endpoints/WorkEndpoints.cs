using System.Security.Claims;
using Expovice.Api.Data;
using Expovice.Api.Infrastructure;
using Expovice.Api.Services;
using Expovice.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Expovice.Api.Endpoints;

/// <summary>The provider's side: incoming requests and the dashboard summary.</summary>
public static class WorkEndpoints
{
    public static IEndpointRouteBuilder MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Work").RequireAuthorization();
        group.MapGet(ApiRoutes.WorkSummary, SummaryAsync);
        group.MapGet(ApiRoutes.WorkRequests, IncomingAsync);
        group.MapPost(ApiRoutes.WorkAccept, (Guid id, ClaimsPrincipal u, ExpoviceDbContext db, ILoggerFactory l, CancellationToken ct) =>
            ChangeAsync(id, u, db, RequestStatus.Pending, RequestStatus.Accepted, l, ct));
        group.MapPost(ApiRoutes.WorkDecline, (Guid id, ClaimsPrincipal u, ExpoviceDbContext db, ILoggerFactory l, CancellationToken ct) =>
            ChangeAsync(id, u, db, RequestStatus.Pending, RequestStatus.Declined, l, ct));
        group.MapPost(ApiRoutes.WorkComplete, (Guid id, ClaimsPrincipal u, ExpoviceDbContext db, ILoggerFactory l, CancellationToken ct) =>
            ChangeAsync(id, u, db, RequestStatus.Accepted, RequestStatus.Completed, l, ct));
        return app;
    }

    private static async Task<Results<Ok<WorkSummaryDto>, ProblemHttpResult>> SummaryAsync(
        ClaimsPrincipal principal, ExpoviceDbContext db, CancellationToken ct)
    {
        var me = principal.UserId();
        if (!await IsProviderAsync(db, me, ct))
        {
            return NotListed();
        }

        // One grouped query instead of three round-trips.
        var byStatus = await db.ServiceRequests.Where(r => r.ProviderId == me)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Value = g.Sum(r => r.Rate) })
            .ToDictionaryAsync(x => x.Status, ct);

        int Count(RequestStatus s) => byStatus.TryGetValue(s, out var x) ? x.Count : 0;
        int Value(RequestStatus s) => byStatus.TryGetValue(s, out var x) ? x.Value : 0;
        return TypedResults.Ok(new WorkSummaryDto(
            Count(RequestStatus.Pending),
            Count(RequestStatus.Accepted),
            Value(RequestStatus.Accepted) + Value(RequestStatus.Completed)));
    }

    private static async Task<Results<Ok<List<IncomingRequestDto>>, ProblemHttpResult>> IncomingAsync(
        ClaimsPrincipal principal, ExpoviceDbContext db, CancellationToken ct)
    {
        var me = principal.UserId();
        var provider = await db.Providers.AsNoTracking().Where(p => p.UserId == me)
            .Select(p => new { p.Latitude, p.Longitude }).SingleOrDefaultAsync(ct);
        if (provider is null)
        {
            return NotListed();
        }

        var rows = await db.ServiceRequests.AsNoTracking()
            .Where(r => r.ProviderId == me)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { Request = r, CustomerName = r.Customer.Name })
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(x => new IncomingRequestDto(
            x.Request.Id,
            ShortName(x.CustomerName),
            Geo.RoundKm(Geo.DistanceKm(provider.Latitude, provider.Longitude, x.Request.Latitude, x.Request.Longitude)),
            x.Request.AddressLine,
            x.Request.Skill,
            x.Request.Note,
            x.Request.Date,
            x.Request.Slot,
            x.Request.Status)).ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangeAsync(
        Guid id, ClaimsPrincipal principal, ExpoviceDbContext db, RequestStatus from, RequestStatus to,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var me = principal.UserId();
        if (!await IsProviderAsync(db, me, ct))
        {
            return NotListed();
        }
        return await RequestEndpoints.Transition(db, db.ServiceRequests.Where(r => r.ProviderId == me), id, [from], to, loggers, ct);
    }

    /// <summary>"Nisha Menon" → "Nisha M." — enough to recognise, less personal data shown.</summary>
    internal static string ShortName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "Customer",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }

    private static Task<bool> IsProviderAsync(ExpoviceDbContext db, Guid userId, CancellationToken ct) =>
        db.Providers.AnyAsync(p => p.UserId == userId, ct);

    private static ProblemHttpResult NotListed() => Problems.Forbidden("List yourself as a pro to see work requests.");
}
