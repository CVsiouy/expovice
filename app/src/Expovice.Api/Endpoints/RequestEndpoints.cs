using System.Security.Claims;
using Expovice.Api.Data;
using Expovice.Api.Infrastructure;
using Expovice.Api.Services;
using Expovice.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Expovice.Api.Endpoints;

public static class RequestEndpoints
{
    public static IEndpointRouteBuilder MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Requests").RequireAuthorization();
        group.MapPost(ApiRoutes.Requests, CreateAsync);
        group.MapGet(ApiRoutes.Requests, ListMineAsync);
        group.MapPost(ApiRoutes.RequestCancel, CancelAsync);
        group.MapPost(ApiRoutes.RequestRate, RateAsync);
        return app;
    }

    private static async Task<Results<Created<ServiceRequestDto>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateServiceRequest body, ClaimsPrincipal principal, ExpoviceDbContext db, IPhotoStore photos,
        TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        var me = principal.UserId();
        var today = IndiaClock.Today(time);
        var v = new Validator()
            .Required(body.Skill, nameof(body.Skill), 64)
            .Check(Catalog.TimeSlots.Contains(body.Slot), nameof(body.Slot), "Pick a valid time slot.")
            .Check(body.Date >= today && body.Date < today.AddDays(Limits.BookingDaysAhead), nameof(body.Date), $"Pick a day within the next {Limits.BookingDaysAhead} days.")
            .Check((body.Note?.Length ?? 0) <= Limits.NoteMaxLength, nameof(body.Note), $"Note must be at most {Limits.NoteMaxLength} characters.")
            .Check(body.ProviderId != me, nameof(body.ProviderId), "You can't request your own service.");
        if (!v.IsValid)
        {
            return v.ToProblem();
        }

        var customer = await db.Users.AsNoTracking().SingleAsync(u => u.Id == me, ct);
        var provider = await db.Providers.Include(p => p.User).AsNoTracking().SingleOrDefaultAsync(p => p.UserId == body.ProviderId, ct);
        if (provider is null)
        {
            return Problems.NotFound("This pro is no longer listed.");
        }
        if (!customer.HasHome)
        {
            return Problems.Invalid("Home", "Set your home address first.");
        }
        if (!provider.IsAccepting)
        {
            return Problems.Conflict($"{provider.User.Name} isn't taking new requests right now.");
        }
        if (!provider.CategoryIds.Contains(body.CategoryId) || !provider.Skills.Contains(body.Skill))
        {
            return Problems.Invalid(nameof(body.Skill), "This pro doesn't offer that service.");
        }

        var request = new ServiceRequest
        {
            CustomerId = me,
            ProviderId = provider.UserId,
            CategoryId = body.CategoryId,
            Skill = body.Skill,
            Note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim(),
            Date = body.Date,
            Slot = body.Slot,
            Status = RequestStatus.Pending,
            AddressLine = customer.HomeLine ?? "",
            Latitude = customer.HomeLat!.Value,
            Longitude = customer.HomeLon!.Value,
            Rate = provider.Rate,
            Unit = provider.Unit,
            CreatedAt = time.GetUtcNow(),
        };

        // Request row + counter bump commit together or not at all.
        await db.InTransactionAsync(async token =>
        {
            db.ServiceRequests.Add(request);
            await db.SaveChangesAsync(token);
            return await db.Providers.Where(p => p.UserId == provider.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.RequestCount, p => p.RequestCount + 1), token);
        }, ct);

        loggers.CreateLogger(nameof(RequestEndpoints)).LogInformation(
            "Request {RequestId} created by {CustomerId} for provider {ProviderId}", request.Id, me, provider.UserId);
        var dto = ToDto(request, provider.User.Name, await photos.GetReadUrlAsync(provider.User.PhotoBlob, ct));
        return TypedResults.Created($"{ApiRoutes.Requests}/{request.Id}", dto);
    }

    private static async Task<Ok<List<ServiceRequestDto>>> ListMineAsync(
        ClaimsPrincipal principal, ExpoviceDbContext db, IPhotoStore photos, CancellationToken ct)
    {
        var me = principal.UserId();
        var rows = await db.ServiceRequests.AsNoTracking()
            .Where(r => r.CustomerId == me)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { Request = r, r.Provider.User.Name, r.Provider.User.PhotoBlob })
            .ToListAsync(ct);

        var list = new List<ServiceRequestDto>(rows.Count);
        foreach (var row in rows)
        {
            list.Add(ToDto(row.Request, row.Name, await photos.GetReadUrlAsync(row.PhotoBlob, ct)));
        }
        return TypedResults.Ok(list);
    }

    private static Task<Results<NoContent, ProblemHttpResult>> CancelAsync(
        Guid id, ClaimsPrincipal principal, ExpoviceDbContext db, ILoggerFactory loggers, CancellationToken ct)
    {
        var me = principal.UserId();
        return Transition(db, db.ServiceRequests.Where(r => r.CustomerId == me), id,
            [RequestStatus.Pending, RequestStatus.Accepted], RequestStatus.Cancelled, loggers, ct);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> RateAsync(
        Guid id, RateServiceRequest body, ClaimsPrincipal principal, ExpoviceDbContext db, ILoggerFactory loggers, CancellationToken ct)
    {
        if (body.Stars is < 1 or > 5)
        {
            return Problems.Invalid(nameof(body.Stars), "Rating must be 1–5 stars.");
        }

        var me = principal.UserId();
        var providerId = await db.ServiceRequests.Where(r => r.Id == id && r.CustomerId == me).Select(r => (Guid?)r.ProviderId).SingleOrDefaultAsync(ct);
        if (providerId is null)
        {
            return Problems.NotFound("Request not found.");
        }

        var rated = await db.InTransactionAsync(async token =>
        {
            // The WHERE makes this a compare-and-set: only one concurrent rating can succeed.
            var rows = await db.ServiceRequests
                .Where(r => r.Id == id && r.Status == RequestStatus.Completed && r.CustomerRating == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CustomerRating, body.Stars), token);
            if (rows > 0)
            {
                await db.Providers.Where(p => p.UserId == providerId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.RatingSum, p => p.RatingSum + body.Stars)
                        .SetProperty(p => p.RatingCount, p => p.RatingCount + 1), token);
            }
            return rows > 0;
        }, ct);
        if (!rated)
        {
            return Problems.Conflict("Only completed requests can be rated, once.");
        }

        loggers.CreateLogger(nameof(RequestEndpoints)).LogInformation("Request {RequestId} rated {Stars}", id, body.Stars);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Race-safe status change: the UPDATE only matches if the row is still in an allowed state,
    /// so two concurrent accepts can't both win. 0 rows → 404 (not yours) or 409 (wrong state).
    /// </summary>
    internal static async Task<Results<NoContent, ProblemHttpResult>> Transition(
        ExpoviceDbContext db, IQueryable<ServiceRequest> scope, Guid id, RequestStatus[] from, RequestStatus to,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var rows = await scope.Where(r => r.Id == id && from.Contains(r.Status))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to), ct);
        if (rows == 0)
        {
            var current = await scope.Where(r => r.Id == id).Select(r => (RequestStatus?)r.Status).SingleOrDefaultAsync(ct);
            return current is null
                ? Problems.NotFound("Request not found.")
                : Problems.Conflict($"This request is already {current.Value.ToString().ToLowerInvariant()}.");
        }

        loggers.CreateLogger(nameof(RequestEndpoints)).LogInformation("Request {RequestId} → {Status}", id, to);
        return TypedResults.NoContent();
    }

    private static ServiceRequestDto ToDto(ServiceRequest r, string providerName, string? providerPhotoUrl) =>
        new(r.Id, r.ProviderId, providerName, providerPhotoUrl, r.CategoryId, r.Skill, r.Note, r.Date, r.Slot, r.Status, r.CustomerRating, r.Rate, r.Unit);
}
