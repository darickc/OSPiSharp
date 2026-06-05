namespace OSPi.Domain.Entities;

/// <summary>
/// Global, runtime-editable controller options (a single persisted row, Id = 1).
/// Distinct from <c>HardwareOptions</c>, which is deploy-time pin config in appsettings.
/// </summary>
public sealed class ControllerSettings
{
    /// <summary>Primary key — always 1 (single-row table).</summary>
    public int Id { get; set; } = 1;

    /// <summary>Global water-level scaling percentage applied to durations (default 100).</summary>
    public int WaterLevelPercent { get; set; } = 100;

    /// <summary>Delay inserted between sequential stations, in seconds.</summary>
    public int StationDelaySeconds { get; set; }

    /// <summary>
    /// IANA/OS timezone id (e.g. <c>America/Boise</c>) used to derive site civil time
    /// <strong>with DST</strong>. This is the source of truth for scheduling time; unlike the
    /// firmware's fixed-offset <c>now_tz()</c>, this app has a full tz database, so the
    /// scheduler tracks daylight-saving transitions. When unset, <see cref="ResolveTimeZone"/>
    /// falls back to the legacy fixed <see cref="UtcOffsetMinutes"/>.
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// Legacy fixed minutes added to UTC for site civil time, <strong>no DST</strong>. Retained
    /// only as the fallback when <see cref="TimeZoneId"/> is unset (mirrors the firmware's
    /// <c>now_tz()</c> model). Example: US Mountain Standard Time = <c>-420</c>.
    /// </summary>
    public int UtcOffsetMinutes { get; set; }

    /// <summary>
    /// Resolve the timezone used to derive civil time. Prefers <see cref="TimeZoneId"/> (DST-aware);
    /// when it is unset or unknown, returns a fixed custom zone built from
    /// <see cref="UtcOffsetMinutes"/> (no DST) so legacy data and tests keep their prior behavior.
    /// The pure scheduling functions never see this — the engine and solar service convert through
    /// it to produce a civil <c>DateTime</c> / per-date offset.
    /// </summary>
    public TimeZoneInfo ResolveTimeZone()
    {
        if (!string.IsNullOrWhiteSpace(TimeZoneId) &&
            TimeZoneInfo.TryFindSystemTimeZoneById(TimeZoneId, out var tz))
        {
            return tz;
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            $"FixedOffset({UtcOffsetMinutes})",
            TimeSpan.FromMinutes(UtcOffsetMinutes),
            displayName: null,
            standardDisplayName: null);
    }

    /// <summary>Master switch for weather-based adjustment.</summary>
    public bool UseWeather { get; set; } = true;

    /// <summary>Default sequencing behavior for new zones.</summary>
    public bool SequentialDefault { get; set; } = true;

    /// <summary>If set, scheduling is suppressed until this instant.</summary>
    public DateTimeOffset? RainDelayUntil { get; set; }

    /// <summary>Site latitude for sunrise/sunset calculation (Phase 2).</summary>
    public double? LocationLatitude { get; set; }

    /// <summary>Site longitude for sunrise/sunset calculation (Phase 2).</summary>
    public double? LocationLongitude { get; set; }
}
