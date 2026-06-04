using OSPi.Domain.Entities;

namespace OSPi.Application.Services;

/// <summary>
/// Resolves sunrise/sunset (as site-civil minute-of-day, 0..1439) for a date, applying the
/// controller's location and fixed UTC offset. The engine queries this once per minute to feed
/// <c>ProgramMatcher.ResolveStartMinute</c>.
/// </summary>
public interface ISolarCalculator
{
    /// <summary>
    /// Sunrise and sunset minute-of-day for <paramref name="date"/>. When the controller has no
    /// location configured, returns sensible defaults (06:00 / 18:00) so sunrise/sunset start
    /// times degrade gracefully rather than failing.
    /// </summary>
    (int SunriseMinute, int SunsetMinute) ForDate(ControllerSettings settings, DateOnly date);
}
