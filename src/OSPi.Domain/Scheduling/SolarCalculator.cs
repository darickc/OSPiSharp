namespace OSPi.Domain.Scheduling;

/// <summary>Classification of a day's sunrise/sunset event.</summary>
public enum SolarEventKind
{
    /// <summary>The sun rises and sets normally.</summary>
    Normal,

    /// <summary>The sun never sets (polar day / midnight sun).</summary>
    PolarDay,

    /// <summary>The sun never rises (polar night).</summary>
    PolarNight,
}

/// <summary>
/// Sunrise and sunset for a day, as site-civil minute-of-day (0..1439). For polar cases the
/// minutes are sentinels; the caller decides policy via <see cref="Kind"/>.
/// </summary>
public readonly record struct SolarTimes(int SunriseMinute, int SunsetMinute, SolarEventKind Kind);

/// <summary>
/// Pure sunrise/sunset computation (NOAA general solar equations). No I/O, no clock.
///
/// <para>The OpenSprinkler firmware does <em>not</em> compute sun times on-device — it
/// receives them from a weather server as a minute-of-day (<c>OPTION_SUNRISE_TIME</c> /
/// <c>OPTION_SUNSET_TIME</c>) and <c>program.cpp starttime_decode</c> consumes those exactly
/// as <see cref="ProgramMatcher.ResolveStartMinute"/> does. This class replaces that server,
/// matching the <em>encoding</em>: a civil-local minute-of-day on the same fixed-offset,
/// no-DST basis as <see cref="CivilInstant"/>.</para>
/// </summary>
public static class SolarCalculator
{
    /// <summary>Official sunrise/sunset zenith (refraction + solar-disk radius), degrees.</summary>
    private const double ZenithDegrees = 90.833;

    /// <summary>
    /// Compute sunrise/sunset for <paramref name="date"/> at the given coordinates, returned
    /// as civil-local minute-of-day using a fixed UTC offset (no DST).
    /// </summary>
    /// <param name="latitudeDeg">Latitude in degrees, positive north.</param>
    /// <param name="longitudeDeg">Longitude in degrees, positive east.</param>
    /// <param name="utcOffsetMinutes">Fixed minutes added to UTC for site civil time.</param>
    public static SolarTimes Compute(double latitudeDeg, double longitudeDeg, DateOnly date, int utcOffsetMinutes)
    {
        double latRad = DegToRad(latitudeDeg);

        // Fractional year (radians), referenced to local noon (NOAA). Slowly varying, so the
        // exact hour within the day is immaterial.
        double daysInYear = DateTime.IsLeapYear(date.Year) ? 366.0 : 365.0;
        double gamma = 2.0 * Math.PI / daysInYear * (date.DayOfYear - 1);

        // Equation of time (minutes) and solar declination (radians).
        double eqTime = 229.18 * (0.000075
            + 0.001868 * Math.Cos(gamma)
            - 0.032077 * Math.Sin(gamma)
            - 0.014615 * Math.Cos(2 * gamma)
            - 0.040849 * Math.Sin(2 * gamma));

        double decl = 0.006918
            - 0.399912 * Math.Cos(gamma)
            + 0.070257 * Math.Sin(gamma)
            - 0.006758 * Math.Cos(2 * gamma)
            + 0.000907 * Math.Sin(2 * gamma)
            - 0.002697 * Math.Cos(3 * gamma)
            + 0.001480 * Math.Sin(3 * gamma);

        // Hour angle at the horizon. Out-of-range cosine means the sun never crosses the
        // horizon that day (polar day/night).
        double cosHa = (Math.Cos(DegToRad(ZenithDegrees)) / (Math.Cos(latRad) * Math.Cos(decl)))
                       - (Math.Tan(latRad) * Math.Tan(decl));
        if (cosHa > 1.0) return new SolarTimes(0, 0, SolarEventKind.PolarNight);   // never rises
        if (cosHa < -1.0) return new SolarTimes(0, 1439, SolarEventKind.PolarDay); // never sets

        double haDeg = RadToDeg(Math.Acos(cosHa));

        // Minutes from UTC midnight (longitude positive east; 4 minutes per degree).
        double sunriseUtc = 720.0 - 4.0 * (longitudeDeg + haDeg) - eqTime;
        double sunsetUtc = 720.0 - 4.0 * (longitudeDeg - haDeg) - eqTime;

        return new SolarTimes(
            WrapToDay(sunriseUtc + utcOffsetMinutes),
            WrapToDay(sunsetUtc + utcOffsetMinutes),
            SolarEventKind.Normal);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
    private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>Round to the nearest minute and wrap into [0, 1439].</summary>
    private static int WrapToDay(double minutes)
    {
        int m = (int)Math.Round(minutes, MidpointRounding.AwayFromZero) % 1440;
        return m < 0 ? m + 1440 : m;
    }
}
