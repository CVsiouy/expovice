using Expovice.Api.Services;
using Expovice.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Expovice.Api.Data;

/// <summary>
/// Development data that mirrors the design prototype: 8 categories × 7 pros around Indiranagar, Bengaluru,
/// generated with the prototype's own deterministic pseudo-random function so names, ratings and distances match.
/// </summary>
public static class SampleDataSeeder
{
    public const double IndiranagarLat = 12.9719;
    public const double IndiranagarLon = 77.6412;
    private const int ProsPerCategory = 7;
    private const int SeedRatingCount = 20;

    private static readonly string[] Names =
    [
        "Ravi Kumar", "Suresh Gowda", "Imran Khan", "Manjunath R", "Anil Sharma", "Prakash Naik", "Deepak Yadav", "Arjun Reddy",
        "Salim Sheikh", "Vinod Pillai", "Ramesh Babu", "Kiran Shetty", "Farhan Ali", "Mahesh Patil", "Naveen Raj", "Joseph D'Souza",
        "Ganesh Murthy", "Rahul Verma", "Santosh Kamath", "Abdul Rahman", "Venkatesh S", "Harish Rao", "Mohan Das", "Sunil Jadhav",
    ];

    private static readonly string[] Walkers = ["Ananya Iyer", "Priya Nair", "Rohan Mehta", "Sneha Kulkarni", "Kavya Rao", "Aditya Bose", "Meera Joshi"];

    private static readonly string[][] LanguageSets =
    [
        ["Kannada", "English"], ["Hindi", "English"], ["Kannada", "Hindi", "English"], ["Tamil", "English"], ["Telugu", "Hindi"],
    ];

    /// <summary>The prototype's <c>rnd</c>: a stable value in [0,1) for any integer seed.</summary>
    public static double Rnd(int n)
    {
        var x = Math.Sin(n * 12.9898 + 78.233) * 43758.5453;
        return x - Math.Floor(x);
    }

    public static async Task SeedAsync(ExpoviceDbContext db, TimeProvider time, ILogger logger, CancellationToken ct)
    {
        if (await db.Providers.AnyAsync(ct))
        {
            return;
        }

        var now = time.GetUtcNow();
        var today = IndiaClock.Now(time).DayOfWeek;
        for (var ci = 0; ci < Catalog.Categories.Count; ci++)
        {
            var c = Catalog.Categories[ci];
            var isDog = c.Id == "dog";
            for (var k = 0; k < ProsPerCategory; k++)
            {
                var s = ci * 31 + k * 7 + 1;
                var pool = isDog ? Walkers : Names;
                var name = pool[(ci * 5 + k * 3) % pool.Length];
                var topSkills = c.Skills.Skip(k % 5).Concat(c.Skills).Take(3).ToList();
                var years = 1 + (int)Math.Floor(Rnd(s + 5) * 14);
                var rating = Math.Round(3.6 + Rnd(s + 1) * 1.4, 1, MidpointRounding.AwayFromZero);
                var distanceKm = Math.Round(0.3 + Rnd(s + 2) * 7.5, 1, MidpointRounding.AwayFromZero);
                var available = Rnd(s + 4) > 0.35;
                var (lat, lon) = Geo.Offset(IndiranagarLat, IndiranagarLon, distanceKm, Rnd(s + 7) * 360);

                var workDays = new HashSet<DayOfWeek>
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
                };
                if (available)
                {
                    workDays.Add(today);
                }
                else
                {
                    workDays.Remove(today);
                }

                var user = new User
                {
                    Name = name,
                    HomeLabel = "Home",
                    HomeLine = "Bengaluru",
                    HomeLat = lat,
                    HomeLon = lon,
                    CreatedAt = now,
                };
                user.Provider = new ProviderProfile
                {
                    UserId = user.Id,
                    Bio = isDog
                        ? $"Walking dogs around Indiranagar for {years} year{(years > 1 ? "s" : "")}. Comfortable with large breeds and anxious pups. Sends a photo update after every walk."
                        : $"{years} years of {c.Name.ToLowerInvariant()} work across east Bengaluru. Specialises in {topSkills[0].ToLowerInvariant()} and {topSkills[1].ToLowerInvariant()}. Carries own tools and cleans up after the job.",
                    Languages = [.. LanguageSets[(ci + k) % LanguageSets.Length]],
                    CategoryIds = [c.Id],
                    Skills = [.. topSkills.Concat(c.Skills).Distinct()],
                    YearsOfExperience = years,
                    Rate = (isDog ? 149 : 199) + (int)Math.Floor(Rnd(s + 3) * 7) * 50,
                    Unit = isDog ? RateUnit.Walk : RateUnit.Visit,
                    RadiusKm = 8,
                    WorkDays = [.. workDays.Order()],
                    From = new TimeOnly(9, 0),
                    To = new TimeOnly(19, 0),
                    IdType = IdDocumentType.Aadhaar,
                    IsVerified = Rnd(s + 6) > 0.25,
                    IsAccepting = true,
                    Latitude = lat,
                    Longitude = lon,
                    RequestCount = 18 + (int)Math.Floor(Rnd(s) * 420),
                    RatingCount = SeedRatingCount,
                    RatingSum = (int)Math.Round(rating * SeedRatingCount),
                    UpdatedAt = now,
                };
                db.Users.Add(user);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} sample providers around Indiranagar", Catalog.Categories.Count * ProsPerCategory);
    }
}
