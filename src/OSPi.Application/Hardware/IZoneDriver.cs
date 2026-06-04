namespace OSPi.Application.Hardware;

/// <summary>
/// The single mutating contract for sprinkler hardware. The engine computes the full
/// desired state of every zone each tick and hands it down; the driver owns no policy,
/// it just applies the bit array. Mirrors the C++ firmware's apply_all_station_bits().
/// </summary>
public interface IZoneDriver
{
    /// <summary>Number of physical zones this driver controls.</summary>
    int ZoneCount { get; }

    /// <summary>
    /// Apply the desired on/off state for all zones at once. Index i == zone i.
    /// Length must equal <see cref="ZoneCount"/>.
    /// </summary>
    void Apply(ReadOnlySpan<bool> zoneStates);
}
