namespace OSPi.Domain.Enums;

/// <summary>
/// How a single start time resolves to a minute-of-day at runtime. Sunrise/sunset
/// kinds are resolved against the controller's configured location in Phase 2.
/// </summary>
public enum StartTimeKind
{
    /// <summary>An absolute minute-of-day (0..1439).</summary>
    FixedMinute = 0,

    /// <summary>Sunrise plus a signed minute offset.</summary>
    SunriseOffset = 1,

    /// <summary>Sunset plus a signed minute offset.</summary>
    SunsetOffset = 2,
}
