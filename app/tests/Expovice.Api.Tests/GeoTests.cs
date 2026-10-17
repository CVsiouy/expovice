using Expovice.Api.Data;
using Expovice.Api.Services;

namespace Expovice.Api.Tests;

public sealed class GeoTests
{
    [Fact]
    public void DistanceKm_BetweenKnownBengaluruPoints_IsAccurate()
    {
        // Indiranagar → MG Road metro is roughly 3.6 km as the crow flies.
        var km = Geo.DistanceKm(12.9719, 77.6412, 12.9755, 77.6070);
        Assert.InRange(km, 3.5, 3.8);
    }

    [Fact]
    public void DistanceKm_SamePoint_IsZero() => Assert.Equal(0, Geo.DistanceKm(12.97, 77.64, 12.97, 77.64), 6);

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(3.2, 90)]
    [InlineData(7.8, 225)]
    public void Offset_RoundTripsThroughDistance(double km, double bearing)
    {
        var (lat, lon) = Geo.Offset(SampleDataSeeder.IndiranagarLat, SampleDataSeeder.IndiranagarLon, km, bearing);
        Assert.Equal(km, Geo.DistanceKm(SampleDataSeeder.IndiranagarLat, SampleDataSeeder.IndiranagarLon, lat, lon), 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(135)]
    [InlineData(300)]
    public void BoxAround_ContainsEveryPointOnTheCircle(double bearing)
    {
        const double radius = 5;
        var box = Geo.BoxAround(12.9719, 77.6412, radius);
        var (lat, lon) = Geo.Offset(12.9719, 77.6412, radius * 0.999, bearing);
        Assert.InRange(lat, box.MinLat, box.MaxLat);
        Assert.InRange(lon, box.MinLon, box.MaxLon);
    }

    [Fact]
    public void Rnd_MatchesPrototypeFormula()
    {
        // JS: x = Math.sin(1 * 12.9898 + 78.233) * 43758.5453; x - Math.floor(x)
        var x = Math.Sin(12.9898 + 78.233) * 43758.5453;
        Assert.Equal(x - Math.Floor(x), SampleDataSeeder.Rnd(1));
        Assert.InRange(SampleDataSeeder.Rnd(42), 0, 1);
    }
}
