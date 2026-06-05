using OSPi.Domain.Entities;
using OSPi.Domain.Scheduling;

namespace OSPi.Application.Services;

/// <summary>
/// Thin wrapper over the pure <see cref="SolarCalculator"/> that applies controller policy:
/// the firmware's default sun times when no location is set, and graceful handling of polar
/// day/night. No I/O — safe to live in the application layer.
/// </summary>
public sealed class SolarCalculatorService : ISolarCalculator
{
    /// <summary>Firmware default sunrise (06:00) when unknown — OpenSprinkler.cpp:1051.</summary>
    private const int DefaultSunrise = 360;

    /// <summary>Firmware default sunset (18:00) when unknown — OpenSprinkler.cpp:1052.</summary>
    private const int DefaultSunset = 1080;

    public (int SunriseMinute, int SunsetMinute) ForDate(ControllerSettings settings, DateOnly date)
    {
        if (settings.LocationLatitude is not { } lat || settings.LocationLongitude is not { } lon)
            return (DefaultSunrise, DefaultSunset);

        // Offset for this civil date from the resolved zone (DST-aware). Sampled at local noon to
        // avoid the midnight DST-transition ambiguity. With the legacy fixed-offset fallback this
        // is constant across dates, preserving prior behavior.
        var tz = settings.ResolveTimeZone();
        int offsetMinutes = (int)tz.GetUtcOffset(date.ToDateTime(new TimeOnly(12, 0))).TotalMinutes;

        var t = SolarCalculator.Compute(lat, lon, date, offsetMinutes);
        return t.Kind switch
        {
            // Midnight sun: treat the whole day as daylight so sunset-anchored runs land at end of day.
            SolarEventKind.PolarDay => (0, 1439),
            // Polar night: no meaningful sun event; fall back to defaults.
            SolarEventKind.PolarNight => (DefaultSunrise, DefaultSunset),
            _ => (t.SunriseMinute, t.SunsetMinute),
        };
    }
}
