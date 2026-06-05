using FluentAssertions;
using OSPi.Application.Services;
using OSPi.Domain.Entities;
using OSPi.Domain.Scheduling;

namespace OSPi.Tests.Scheduling;

/// <summary>
/// Covers how <see cref="ControllerSettings.ResolveTimeZone"/> derives the scheduling timezone
/// (DST-aware when an id is set, legacy fixed-offset fallback otherwise) and how
/// <see cref="SolarCalculatorService"/> applies the per-date offset from it.
/// </summary>
public class TimeZoneResolutionTests
{
    // Local noon on a date, interpreted in the resolved zone (Unspecified kind).
    private static DateTime Noon(int year, int month, int day) => new(year, month, day, 12, 0, 0);

    [Fact]
    public void Resolve_uses_timezone_id_and_tracks_dst()
    {
        var settings = new ControllerSettings { TimeZoneId = "America/Denver" };
        var tz = settings.ResolveTimeZone();

        tz.GetUtcOffset(Noon(2026, 7, 1)).Should().Be(TimeSpan.FromMinutes(-360)); // MDT
        tz.GetUtcOffset(Noon(2026, 1, 1)).Should().Be(TimeSpan.FromMinutes(-420)); // MST
    }

    [Fact]
    public void Resolve_falls_back_to_fixed_offset_when_id_unset()
    {
        var settings = new ControllerSettings { TimeZoneId = null, UtcOffsetMinutes = -420 };
        var tz = settings.ResolveTimeZone();

        tz.SupportsDaylightSavingTime.Should().BeFalse();
        tz.GetUtcOffset(Noon(2026, 7, 1)).Should().Be(TimeSpan.FromMinutes(-420));
        tz.GetUtcOffset(Noon(2026, 1, 1)).Should().Be(TimeSpan.FromMinutes(-420));
    }

    [Fact]
    public void Resolve_falls_back_to_fixed_offset_when_id_is_unknown()
    {
        var settings = new ControllerSettings { TimeZoneId = "Not/ARealZone", UtcOffsetMinutes = 60 };

        settings.ResolveTimeZone().GetUtcOffset(Noon(2026, 1, 1)).Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void Solar_service_applies_the_dst_offset_for_the_requested_date()
    {
        // Rexburg ID with a DST-aware zone: July must use -360, January -420. Comparing against the
        // pure calculator with the expected offset proves the per-date offset is wired through.
        const double lat = 43.826, lon = -111.789;
        var calc = new SolarCalculatorService();
        var settings = new ControllerSettings
        {
            LocationLatitude = lat,
            LocationLongitude = lon,
            TimeZoneId = "America/Denver",
        };

        var july = new DateOnly(2026, 7, 1);
        var expJuly = SolarCalculator.Compute(lat, lon, july, -360);
        calc.ForDate(settings, july).Should().Be((expJuly.SunriseMinute, expJuly.SunsetMinute));

        var jan = new DateOnly(2026, 1, 1);
        var expJan = SolarCalculator.Compute(lat, lon, jan, -420);
        calc.ForDate(settings, jan).Should().Be((expJan.SunriseMinute, expJan.SunsetMinute));
    }
}
