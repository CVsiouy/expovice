using System.Globalization;
using Expovice.Contracts;

namespace Expovice.App.Core.Ui;

/// <summary>Display formatting shared by every view model (DRY). Pure functions: thread-safe.</summary>
public static class Formatting
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    /// <summary>Indian digit grouping (1,00,000) without depending on the device having the en-IN culture.</summary>
    private static readonly NumberFormatInfo IndianGrouping = new() { NumberGroupSeparator = ",", NumberGroupSizes = [3, 2] };

    /// <summary>India has no daylight saving, so a fixed +05:30 offset is exact and avoids time-zone lookups that vary by OS.</summary>
    public static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    public static DateOnly IndiaToday(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().ToOffset(IndiaOffset).DateTime);

    public static string Rupees(int amount) => "₹" + amount.ToString("#,0", IndianGrouping);

    public static string Distance(double km) => km.ToString("0.0", En) + " km";

    public static string Rating(double rating) => rating.ToString("0.0", En);

    public static string Unit(RateUnit unit) => unit switch
    {
        RateUnit.Hour => "hour",
        RateUnit.Walk => "walk",
        _ => "visit",
    };

    public static string FirstName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    /// <summary>"9876543210" → "98765 43210".</summary>
    public static string PhoneLocal(string digits) => digits.Length > 5 ? $"{digits[..5]} {digits[5..]}" : digits;

    /// <summary>"+919876543210" → "+91 98765 43210".</summary>
    public static string PhoneDisplay(string? e164) =>
        e164 is { Length: 13 } && e164.StartsWith("+91", StringComparison.Ordinal) ? "+91 " + PhoneLocal(e164[3..]) : e164 ?? string.Empty;

    /// <summary>"9 AM", "7:30 PM".</summary>
    public static string Hour(TimeOnly t) => t.ToString(t.Minute == 0 ? "h tt" : "h:mm tt", En);

    /// <summary>Inverse of <see cref="Hour"/> for the "9 AM" strings in <see cref="Choices.Hours"/>.</summary>
    public static TimeOnly ParseHour(string text) =>
        TimeOnly.ParseExact(text, ["h tt", "h:mm tt"], En, DateTimeStyles.None);

    public static string ShortDay(DayOfWeek d) => En.DateTimeFormat.GetAbbreviatedDayName(d);

    /// <summary>Monday-first ordering used across the UI.</summary>
    public static IReadOnlyList<DayOfWeek> Week { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>"Mon–Sat" for a contiguous run, otherwise "Mon, Wed, Fri".</summary>
    public static string DayRange(IEnumerable<DayOfWeek> days)
    {
        var idx = days.Select(d => ((int)d + 6) % 7).Distinct().Order().ToArray();
        if (idx.Length == 0)
        {
            return "No days set";
        }

        if (idx.Length == 7)
        {
            return "Every day";
        }

        var contiguous = idx[^1] - idx[0] == idx.Length - 1;
        return contiguous && idx.Length > 2
            ? $"{ShortDay(Week[idx[0]])}–{ShortDay(Week[idx[^1]])}"
            : string.Join(", ", idx.Select(i => ShortDay(Week[i])));
    }

    /// <summary>"Today · 9–11 AM", "Tomorrow · …", otherwise "Mon 19 Oct · …" (matches the prototype).</summary>
    public static string When(DateOnly date, DateOnly today, string slot)
    {
        var day = (date.DayNumber - today.DayNumber) switch
        {
            0 => "Today",
            1 => "Tomorrow",
            _ => date.ToString("ddd d MMM", En),
        };
        return $"{day} · {slot}";
    }

    /// <summary>"12th Main, Indiranagar, Bengaluru" → "Indiranagar".</summary>
    public static string AreaFromLine(string line)
    {
        var parts = line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "you",
            1 => parts[0],
            _ => parts[^2],
        };
    }

    public static string Plural(int n, string singular, string? plural = null) => n == 1 ? singular : plural ?? singular + "s";
}

/// <summary>Option lists from the prototype.</summary>
public static class Choices
{
    public static IReadOnlyList<string> Hours { get; } =
        ["6 AM", "7 AM", "8 AM", "9 AM", "10 AM", "11 AM", "12 PM", "1 PM", "2 PM", "3 PM", "4 PM", "5 PM", "6 PM", "7 PM", "8 PM", "9 PM", "10 PM"];

    public static IReadOnlyList<(IdDocumentType Type, string Label)> IdTypes { get; } =
        [(IdDocumentType.Aadhaar, "Aadhaar"), (IdDocumentType.Pan, "PAN"), (IdDocumentType.DrivingLicence, "Driving licence")];

    /// <summary>"Under ₹400" filter: rate strictly below 400.</summary>
    public const int BudgetMaxRate = 399;
}
