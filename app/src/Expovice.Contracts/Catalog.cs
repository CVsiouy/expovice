using System.Collections.Frozen;

namespace Expovice.Contracts;

/// <summary>A service category shown as a card on Home (Electrician, Plumber, ...).</summary>
/// <param name="Id">Stable key stored in the database and used in URLs.</param>
/// <param name="Name">Display name.</param>
/// <param name="Icon">Lucide icon key understood by the app's icon control.</param>
/// <param name="Skills">Skills a pro in this category can offer.</param>
public sealed record ServiceCategory(string Id, string Name, string Icon, IReadOnlyList<string> Skills);

/// <summary>Static reference data shared by client and server.</summary>
public static class Catalog
{
    public static IReadOnlyList<ServiceCategory> Categories { get; } =
    [
        new("electrician", "Electrician", "zap", ["Wiring", "Switchboards", "Fan & light fitting", "Inverter setup", "MCB repair"]),
        new("plumber", "Plumber", "wrench", ["Leak repair", "Tap fitting", "Drain unclogging", "Tank cleaning", "Geyser install"]),
        new("carpenter", "Carpenter", "hammer", ["Furniture repair", "Door & lock fitting", "Modular assembly", "Polishing", "Custom shelves"]),
        new("ac", "AC repair", "snow", ["Gas refill", "Split AC service", "Installation", "Cooling issues", "PCB repair"]),
        new("fridge", "Fridge repair", "fridge", ["Not cooling", "Compressor", "Thermostat", "Gas charging", "Door seal"]),
        new("washer", "Washing machine", "washer", ["Drum issues", "Drain pump", "Front-load service", "Top-load service", "Motor repair"]),
        new("dog", "Dog walking", "paw", ["Daily walks", "Pet sitting", "Feeding visits", "Puppy training", "Large breeds"]),
        new("painter", "Painter", "roller", ["Wall painting", "Texture finish", "Waterproofing", "Wood polish", "Touch-ups"]),
    ];

    /// <summary>O(1) read-optimised lookup; FrozenDictionary is built once and never mutated.</summary>
    public static FrozenDictionary<string, ServiceCategory> ById { get; } =
        Categories.ToFrozenDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every skill across all categories, for validating provider input.</summary>
    public static FrozenSet<string> AllSkills { get; } =
        Categories.SelectMany(c => c.Skills).ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> Languages { get; } = ["English", "Hindi", "Kannada", "Tamil", "Telugu", "Marathi"];

    /// <summary>Two-hour visit windows a customer can pick.</summary>
    public static IReadOnlyList<string> TimeSlots { get; } = ["9–11 AM", "11–1 PM", "1–3 PM", "3–5 PM", "5–7 PM", "7–9 PM"];

    public static string CategoryName(string id) => ById.TryGetValue(id, out var c) ? c.Name : id;
}

/// <summary>Validation limits enforced by the API and mirrored in the app's UI.</summary>
public static class Limits
{
    public const int NameMaxLength = 80;
    public const int BioMaxLength = 600;
    public const int NoteMaxLength = 500;
    public const int AddressMaxLength = 160;
    public const int OtpLength = 6;
    public const int OtpMaxAttempts = 5;
    public const int OtpTtlMinutes = 5;
    public const int OtpResendSeconds = 30;
    public const int RateMin = 50;
    public const int RateMax = 99_999;
    public const int RadiusMinKm = 1;
    public const int RadiusMaxKm = 15;
    public const int YearsMax = 40;
    public const int MaxPhotoBytes = 5 * 1024 * 1024;
    public const int DefaultSearchRadiusKm = 5;
    public const int BookingDaysAhead = 4;
}
