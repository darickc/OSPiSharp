using OSPi.Domain.Enums;

namespace OSPi.Domain.Entities;

/// <summary>
/// A physical sprinkler zone (firmware "station"). There are exactly 16 on this
/// OSPi-clone; they are seeded once and edited, never created or deleted.
/// </summary>
public sealed class Zone
{
    /// <summary>Surrogate primary key (the relational identity).</summary>
    public int Id { get; set; }

    /// <summary>
    /// Stable hardware bit index (0..15) — the position the engine's <c>bool[16]</c>
    /// indexes and the shift-register driver writes. Unique and immutable.
    /// </summary>
    public int HardwareBit { get; set; }

    /// <summary>User-facing zone name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Sequencing group governing how this zone runs relative to others.</summary>
    public ZoneGroup Group { get; set; } = ZoneGroup.Sequential0;

    /// <summary>Whether this zone activates master station 1 while running.</summary>
    public bool BoundToMaster1 { get; set; }

    /// <summary>Whether this zone activates master station 2 while running.</summary>
    public bool BoundToMaster2 { get; set; }

    /// <summary>Whether the zone is disabled (never scheduled or run).</summary>
    public bool Disabled { get; set; }

    /// <summary>Whether the zone ignores an active rain delay.</summary>
    public bool IgnoreRain { get; set; }

    /// <summary>Whether the zone ignores sensor input.</summary>
    public bool IgnoreSensor { get; set; }

    /// <summary>Per-program durations referencing this zone.</summary>
    public ICollection<ProgramZoneDuration> ProgramDurations { get; set; } = new List<ProgramZoneDuration>();
}
