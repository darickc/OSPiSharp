using FluentAssertions;
using OSPi.Application.Services;
using OSPi.Domain.Entities;
using OSPi.Domain.Scheduling;

namespace OSPi.Tests.Scheduling;

/// <summary>
/// Validates the pure NOAA sunrise/sunset computation against almanac values (generous
/// tolerance, since the gamma approximation and reference tables differ by a few minutes) and
/// against robust correctness properties (day length, offset shift, polar day/night).
/// </summary>
public class SolarCalculatorTests
{
    private const int Tol = 6; // minutes

    [Fact]
    public void London_equinox_sunrise_near_6am_and_day_about_12h()
    {
        // 2024-03-20, London. GMT (BST has not started); offset 0.
        var t = SolarCalculator.Compute(51.5074, -0.1278, new DateOnly(2024, 3, 20), 0);

        t.Kind.Should().Be(SolarEventKind.Normal);
        t.SunriseMinute.Should().BeCloseTo(362, Tol);   // ~06:02 GMT
        t.SunsetMinute.Should().BeCloseTo(1094, Tol);   // ~18:14 GMT
    }

    [Fact]
    public void Rexburg_summer_solstice_has_a_long_day_symmetric_about_solar_noon()
    {
        // 2024-06-20, Rexburg ID (lon -111.8°, deep in the west of MST), civil MDT (fixed -360,
        // no DST in our model). From first principles solar noon ≈ 13:29 and the half-day ≈ 7h44m,
        // giving ~05:44 sunrise / ~21:13 sunset and >15h of daylight.
        var t = SolarCalculator.Compute(43.826, -111.789, new DateOnly(2024, 6, 20), -360);

        t.Kind.Should().Be(SolarEventKind.Normal);
        (t.SunsetMinute - t.SunriseMinute).Should().BeGreaterThan(900); // > 15h of daylight
        var solarNoon = (t.SunriseMinute + t.SunsetMinute) / 2;
        solarNoon.Should().BeCloseTo(809, 10);          // ~13:29 MDT, dominated by the western longitude
        t.SunsetMinute.Should().BeCloseTo(1273, Tol);   // ~21:13 MDT
    }

    [Fact]
    public void Equator_equinox_day_length_is_about_twelve_hours()
    {
        var t = SolarCalculator.Compute(0.0, 0.0, new DateOnly(2024, 3, 20), 0);

        t.Kind.Should().Be(SolarEventKind.Normal);
        (t.SunsetMinute - t.SunriseMinute).Should().BeCloseTo(720, 12);
    }

    [Fact]
    public void Utc_offset_shifts_civil_times_by_the_offset()
    {
        var baseLine = SolarCalculator.Compute(40.0, -100.0, new DateOnly(2024, 5, 1), 0);
        var shifted = SolarCalculator.Compute(40.0, -100.0, new DateOnly(2024, 5, 1), 60);

        Wrap(baseLine.SunriseMinute + 60).Should().Be(shifted.SunriseMinute);
        Wrap(baseLine.SunsetMinute + 60).Should().Be(shifted.SunsetMinute);
    }

    [Fact]
    public void High_arctic_is_polar_day_in_summer_and_polar_night_in_winter()
    {
        var summer = SolarCalculator.Compute(78.22, 15.65, new DateOnly(2024, 6, 21), 60);
        var winter = SolarCalculator.Compute(78.22, 15.65, new DateOnly(2024, 12, 21), 60);

        summer.Kind.Should().Be(SolarEventKind.PolarDay);
        winter.Kind.Should().Be(SolarEventKind.PolarNight);
    }

    [Fact]
    public void Service_uses_firmware_defaults_when_location_is_unset()
    {
        var calc = new SolarCalculatorService();
        var settings = new ControllerSettings { LocationLatitude = null, LocationLongitude = null };

        calc.ForDate(settings, new DateOnly(2024, 6, 21)).Should().Be((360, 1080));
    }

    [Fact]
    public void Service_maps_polar_day_to_full_daylight_and_polar_night_to_defaults()
    {
        var calc = new SolarCalculatorService();
        var arctic = new ControllerSettings { LocationLatitude = 78.22, LocationLongitude = 15.65, UtcOffsetMinutes = 60 };

        calc.ForDate(arctic, new DateOnly(2024, 6, 21)).Should().Be((0, 1439));
        calc.ForDate(arctic, new DateOnly(2024, 12, 21)).Should().Be((360, 1080));
    }

    private static int Wrap(int m) => ((m % 1440) + 1440) % 1440;
}
