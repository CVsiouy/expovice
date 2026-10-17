namespace Expovice.Api.Services;

/// <summary>Pure, thread-safe geographic helpers (no state).</summary>
public static class Geo
{
    private const double EarthRadiusKm = 6371.0088;
    private const double KmPerDegreeLat = EarthRadiusKm * Math.PI / 180;

    /// <summary>Great-circle distance between two points in kilometres (Haversine formula).</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
            + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>
    /// A lat/lon rectangle that fully contains the circle of <paramref name="radiusKm"/>.
    /// Cheap to evaluate in SQL against an index; exact distance is checked afterwards.
    /// </summary>
    public static BoundingBox BoxAround(double lat, double lon, double radiusKm)
    {
        var dLat = radiusKm / KmPerDegreeLat;
        var cosLat = Math.Max(Math.Cos(ToRadians(lat)), 1e-6);
        var dLon = radiusKm / (KmPerDegreeLat * cosLat);
        return new BoundingBox(lat - dLat, lat + dLat, lon - dLon, lon + dLon);
    }

    /// <summary>Moves a point <paramref name="distanceKm"/> along <paramref name="bearingDegrees"/>.</summary>
    public static (double Lat, double Lon) Offset(double lat, double lon, double distanceKm, double bearingDegrees)
    {
        var angular = distanceKm / EarthRadiusKm;
        var bearing = ToRadians(bearingDegrees);
        var lat1 = ToRadians(lat);
        var lon1 = ToRadians(lon);
        var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(angular) + Math.Cos(lat1) * Math.Sin(angular) * Math.Cos(bearing));
        var lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(lat1), Math.Cos(angular) - Math.Sin(lat1) * Math.Sin(lat2));
        return (ToDegrees(lat2), ToDegrees(lon2));
    }

    public static double RoundKm(double km) => Math.Round(km, 1);

    private static double ToRadians(double deg) => deg * Math.PI / 180;

    private static double ToDegrees(double rad) => rad * 180 / Math.PI;
}

public readonly record struct BoundingBox(double MinLat, double MaxLat, double MinLon, double MaxLon);
