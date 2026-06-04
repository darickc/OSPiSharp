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
