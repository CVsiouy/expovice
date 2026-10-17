using System.Security.Claims;
using Expovice.Api.Data;
using Expovice.Api.Infrastructure;
using Expovice.Api.Services;
using Expovice.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Expovice.Api.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Me").RequireAuthorization();

        group.MapGet(ApiRoutes.Me, GetMeAsync);
        group.MapPut(ApiRoutes.MeName, UpdateNameAsync);
        group.MapPut(ApiRoutes.MeHome, UpdateHomeAsync);
        group.MapPost(ApiRoutes.MePhoto, UploadPhotoAsync).DisableAntiforgery();
        group.MapGet(ApiRoutes.MeProvider, GetProviderAsync);
        group.MapPut(ApiRoutes.MeProvider, UpsertProviderAsync);
        group.MapPut(ApiRoutes.MeProviderAccepting, SetAcceptingAsync);
        return app;
    }

    private static async Task<Results<Ok<MeDto>, ProblemHttpResult>> GetMeAsync(
        ClaimsPrincipal principal, ExpoviceDbContext db, IPhotoStore photos, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Provider).SingleOrDefaultAsync(u => u.Id == principal.UserId(), ct);
        return user is null ? Problems.NotFound("Account not found.") : TypedResults.Ok(await ToMeAsync(user, photos, ct));
    }

    private static async Task<Results<Ok<MeDto>, ValidationProblem, ProblemHttpResult>> UpdateNameAsync(
        UpdateNameRequest body, ClaimsPrincipal principal, ExpoviceDbContext db, IPhotoStore photos, CancellationToken ct)
    {
        var v = new Validator().Required(body.Name, nameof(body.Name), Limits.NameMaxLength);
        if (!v.IsValid)
        {
            return v.ToProblem();
        }

        var user = await db.Users.Include(u => u.Provider).SingleOrDefaultAsync(u => u.Id == principal.UserId(), ct);
        if (user is null)
        {
            return Problems.NotFound("Account not found.");
        }

        user.Name = body.Name.Trim();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToMeAsync(user, photos, ct));
    }

    private static async Task<Results<Ok<MeDto>, ValidationProblem, ProblemHttpResult>> UpdateHomeAsync(
        HomeAddressDto body, ClaimsPrincipal principal, ExpoviceDbContext db, IPhotoStore photos, CancellationToken ct)
    {
        var v = new Validator()
            .Required(body.Label, nameof(body.Label), 40)
            .Required(body.Line, nameof(body.Line), Limits.AddressMaxLength)
            .Check(body.Latitude is >= -90 and <= 90, nameof(body.Latitude), "Latitude is out of range.")
            .Check(body.Longitude is >= -180 and <= 180, nameof(body.Longitude), "Longitude is out of range.");
        if (!v.IsValid)
        {
            return v.ToProblem();
        }

        var user = await db.Users.Include(u => u.Provider).SingleOrDefaultAsync(u => u.Id == principal.UserId(), ct);
        if (user is null)
        {
            return Problems.NotFound("Account not found.");
        }

        (user.HomeLabel, user.HomeLine, user.HomeLat, user.HomeLon) = (body.Label.Trim(), body.Line.Trim(), body.Latitude, body.Longitude);
        if (user.Provider is { } p)
        {
            (p.Latitude, p.Longitude) = (body.Latitude, body.Longitude);
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToMeAsync(user, photos, ct));
    }

    private static async Task<Results<Ok<PhotoUploadResponse>, ValidationProblem, ProblemHttpResult>> UploadPhotoAsync(
        PhotoSlot slot, IFormFile file, ClaimsPrincipal principal, ExpoviceDbContext db, IPhotoStore photos,
        ILoggerFactory loggers, CancellationToken ct)
    {
        if (file.Length is 0 or > Limits.MaxPhotoBytes)
        {
            return Problems.Invalid(nameof(file), $"Photo must be smaller than {Limits.MaxPhotoBytes / (1024 * 1024)} MB.");
        }
        if (file.ContentType is null || !PhotoTypes.Extensions.ContainsKey(file.ContentType))
        {
            return Problems.Invalid(nameof(file), "Use a JPEG, PNG or WebP photo.");
        }

        await using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, ct);
        if (!PhotoTypes.HasImageSignature(stream.GetBuffer().AsSpan(0, (int)Math.Min(stream.Length, 16))))
        {
            return Problems.Invalid(nameof(file), "That file isn't a valid image.");
        }

        var user = await db.Users.Include(u => u.Provider).SingleOrDefaultAsync(u => u.Id == principal.UserId(), ct);
        if (user is null)
        {
            return Problems.NotFound("Account not found.");
        }
        if (slot != PhotoSlot.Profile && user.Provider is null)
        {
            return Problems.Conflict("Publish your provider profile before adding work or ID photos.");
        }

        stream.Position = 0;
        var blob = await photos.SaveAsync(user.Id, slot, stream, file.ContentType, ct);
        var previous = Swap(user, slot, blob);
        await db.SaveChangesAsync(ct);
        if (previous is not null)
        {
            await photos.DeleteAsync(slot, previous, ct);
        }

        loggers.CreateLogger(nameof(MeEndpoints)).LogInformation("User {UserId} uploaded {Slot} photo", user.Id, slot);
        // ID documents are private: never hand back a URL for them.
        var url = slot == PhotoSlot.IdFront ? null : await photos.GetReadUrlAsync(blob, ct);
        return TypedResults.Ok(new PhotoUploadResponse(slot, url));
    }

    /// <summary>Stores the new blob name in the slot's column and returns the old one (to delete).</summary>
    private static string? Swap(User user, PhotoSlot slot, string blob)
    {
        string? old;
        var p = user.Provider;
        switch (slot)
        {
            case PhotoSlot.Profile: (old, user.PhotoBlob) = (user.PhotoBlob, blob); break;
            case PhotoSlot.Work1: (old, p!.WorkPhoto1Blob) = (p!.WorkPhoto1Blob, blob); break;
            case PhotoSlot.Work2: (old, p!.WorkPhoto2Blob) = (p!.WorkPhoto2Blob, blob); break;
            case PhotoSlot.Work3: (old, p!.WorkPhoto3Blob) = (p!.WorkPhoto3Blob, blob); break;
            case PhotoSlot.IdFront: (old, p!.IdDocumentBlob) = (p!.IdDocumentBlob, blob); break;
            default: throw new ArgumentOutOfRangeException(nameof(slot));
        }
        return old;
    }

    private static async Task<Results<Ok<ProviderProfileDto>, ProblemHttpResult>> GetProviderAsync(
        ClaimsPrincipal principal, ExpoviceDbContext db, IPhotoStore photos, CancellationToken ct)
    {
        var p = await db.Providers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == principal.UserId(), ct);
        return p is null ? Problems.NotFound("You are not listed yet.") : TypedResults.Ok(await ToProfileAsync(p, photos, ct));
    }

    private static async Task<Results<Ok<ProviderProfileDto>, ValidationProblem, ProblemHttpResult>> UpsertProviderAsync(
        UpsertProviderProfileRequest body, ClaimsPrincipal principal, ExpoviceDbContext db, IPhotoStore photos,
        TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        var v = ValidateProfile(body);
        if (!v.IsValid)
        {
            return v.ToProblem();
        }

        var user = await db.Users.Include(u => u.Provider).SingleOrDefaultAsync(u => u.Id == principal.UserId(), ct);
        if (user is null)
        {
            return Problems.NotFound("Account not found.");
        }
        if (!user.HasHome)
        {
            return Problems.Invalid("Home", "Set your home location before listing yourself.");
        }

        var isNew = user.Provider is null;
        var p = user.Provider ??= new ProviderProfile { UserId = user.Id, IsAccepting = true };
        user.Name = body.Name.Trim();
        p.Bio = body.Bio?.Trim() ?? "";
        p.Languages = [.. body.Languages.Distinct()];
        p.CategoryIds = [.. body.CategoryIds.Distinct()];
        p.Skills = [.. body.Skills.Distinct()];
        p.YearsOfExperience = body.YearsOfExperience;
        p.Rate = body.Rate;
        p.Unit = body.Unit;
        p.RadiusKm = body.RadiusKm;
        p.WorkDays = [.. body.WorkDays.Distinct().Order()];
        p.From = body.From;
        p.To = body.To;
        p.IdType = body.IdType;
        p.Latitude = user.HomeLat!.Value;
        p.Longitude = user.HomeLon!.Value;
        p.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        loggers.CreateLogger(nameof(MeEndpoints)).LogInformation(
            "User {UserId} {Action} provider listing in {Categories}", user.Id, isNew ? "published" : "updated", string.Join(',', p.CategoryIds));
        return TypedResults.Ok(await ToProfileAsync(p, photos, ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetAcceptingAsync(
        SetAcceptingRequest body, ClaimsPrincipal principal, ExpoviceDbContext db, CancellationToken ct)
    {
        var me = principal.UserId();
        var rows = await db.Providers.Where(p => p.UserId == me)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsAccepting, body.IsAccepting), ct);
        return rows == 0 ? Problems.NotFound("You are not listed yet.") : TypedResults.NoContent();
    }

    internal static Validator ValidateProfile(UpsertProviderProfileRequest b)
    {
        var categories = b.CategoryIds ?? [];
        var allowedSkills = categories.Where(Catalog.ById.ContainsKey).SelectMany(c => Catalog.ById[c].Skills).ToHashSet(StringComparer.Ordinal);
        return new Validator()
            .Required(b.Name, nameof(b.Name), Limits.NameMaxLength)
            .Check((b.Bio?.Length ?? 0) <= Limits.BioMaxLength, nameof(b.Bio), $"Bio must be at most {Limits.BioMaxLength} characters.")
            .Check(b.Languages is { Count: > 0 } && b.Languages.All(Catalog.Languages.Contains), nameof(b.Languages), "Pick at least one supported language.")
            .Check(categories.Count > 0, nameof(b.CategoryIds), "Pick at least one service.")
            .Check(categories.All(Catalog.ById.ContainsKey), nameof(b.CategoryIds), "Unknown service category.")
            .Check(b.Skills is not null && b.Skills.All(allowedSkills.Contains), nameof(b.Skills), "Skills must belong to the services you picked.")
            .Check(b.YearsOfExperience is >= 0 and <= Limits.YearsMax, nameof(b.YearsOfExperience), $"Experience must be 0–{Limits.YearsMax} years.")
            .Check(b.Rate is >= Limits.RateMin and <= Limits.RateMax, nameof(b.Rate), $"Rate must be ₹{Limits.RateMin}–₹{Limits.RateMax}.")
            .Check(Enum.IsDefined(b.Unit), nameof(b.Unit), "Unknown rate unit.")
            .Check(b.RadiusKm is >= Limits.RadiusMinKm and <= Limits.RadiusMaxKm, nameof(b.RadiusKm), $"Radius must be {Limits.RadiusMinKm}–{Limits.RadiusMaxKm} km.")
            .Check(b.WorkDays is { Count: > 0 }, nameof(b.WorkDays), "Pick at least one working day.")
            .Check(b.From < b.To, nameof(b.To), "End time must be after start time.")
            .Check(Enum.IsDefined(b.IdType), nameof(b.IdType), "Unknown ID type.");
    }

    internal static async Task<MeDto> ToMeAsync(User u, IPhotoStore photos, CancellationToken ct) =>
        new(
            u.Id,
            u.Name,
            u.PhoneNumber,
            await photos.GetReadUrlAsync(u.PhotoBlob, ct),
            u.HasHome ? new HomeAddressDto(u.HomeLabel ?? "Home", u.HomeLine ?? "", u.HomeLat!.Value, u.HomeLon!.Value) : null,
            u.Provider is not null);

    private static async Task<ProviderProfileDto> ToProfileAsync(ProviderProfile p, IPhotoStore photos, CancellationToken ct) =>
        new(
            p.Bio,
            p.Languages,
            p.CategoryIds,
            p.Skills,
            p.YearsOfExperience,
            p.Rate,
            p.Unit,
            p.RadiusKm,
            p.WorkDays,
            p.From,
            p.To,
            p.IdType,
            p.IdDocumentBlob is not null,
            p.IsVerified,
            p.IsAccepting,
            await WorkPhotoUrlsAsync(p, photos, ct));

    internal static async Task<IReadOnlyList<string>> WorkPhotoUrlsAsync(ProviderProfile p, IPhotoStore photos, CancellationToken ct)
    {
        var urls = new List<string>();
        foreach (var blob in p.WorkPhotoBlobs)
        {
            if (await photos.GetReadUrlAsync(blob, ct) is { } url)
            {
                urls.Add(url);
            }
        }
        return urls;
    }
}
