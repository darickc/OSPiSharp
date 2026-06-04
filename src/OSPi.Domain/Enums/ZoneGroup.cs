namespace OSPi.Domain.Enums;

/// <summary>
/// Sequencing group for a zone. The four sequential groups keep the firmware's
/// numeric identity (0..3) because the Phase 2 scheduler keys on them; parallel
/// and independent use distinct stable values.
/// </summary>
public enum ZoneGroup
{
    /// <summary>Sequential group 0 — runs one-at-a-time with other group-0 zones.</summary>
    Sequential0 = 0,

    /// <summary>Sequential group 1.</summary>
    Sequential1 = 1,

    /// <summary>Sequential group 2.</summary>
    Sequential2 = 2,

    /// <summary>Sequential group 3.</summary>
    Sequential3 = 3,

    /// <summary>Runs in parallel with other zones (firmware parallel group).</summary>
    Parallel = 100,

    /// <summary>Runs independently, ignoring sequencing entirely.</summary>
    Independent = 101,
}
