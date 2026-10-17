namespace Expovice.Api.Services;

/// <summary>Business dates/days are evaluated in India Standard Time, regardless of server time zone.</summary>
public static class IndiaClock
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    public static DateTime Now(TimeProvider time) => TimeZoneInfo.ConvertTime(time.GetUtcNow(), Ist).DateTime;

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(Now(time));
}
