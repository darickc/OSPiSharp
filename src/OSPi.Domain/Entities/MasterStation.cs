namespace OSPi.Domain.Entities;

/// <summary>
/// A master (pump/valve) station that activates while bound zones run, with optional
/// lead/lag adjustments. There are two; both rows are seeded.
/// </summary>
public sealed class MasterStation
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Master number (1 or 2). Unique.</summary>
    public int MasterIndex { get; set; }

    /// <summary>Which zone acts as this master, or null if unconfigured.</summary>
    public int? ZoneId { get; set; }

    /// <summary>Zone navigation (null when unconfigured).</summary>
    public Zone? Zone { get; set; }

    /// <summary>Seconds the master turns on before/after a bound zone starts (signed).</summary>
    public int OnAdjustSeconds { get; set; }

    /// <summary>Seconds the master turns off before/after a bound zone stops (signed).</summary>
    public int OffAdjustSeconds { get; set; }
}
