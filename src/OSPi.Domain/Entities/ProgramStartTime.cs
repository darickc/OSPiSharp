using OSPi.Domain.Enums;

namespace OSPi.Domain.Entities;

/// <summary>
/// A single start time for a program (owned by <see cref="Program"/>). A program has
/// up to four. In <see cref="StartTimeType.Repeating"/> mode only slot 0 is the anchor.
/// </summary>
public sealed class ProgramStartTime
{
    /// <summary>Ordering slot within the program (0..3).</summary>
    public int Slot { get; set; }

    /// <summary>How <see cref="Value"/> is interpreted.</summary>
    public StartTimeKind Kind { get; set; } = StartTimeKind.FixedMinute;

    /// <summary>
    /// For <see cref="StartTimeKind.FixedMinute"/>: minute-of-day (0..1439).
    /// For sunrise/sunset kinds: a signed minute offset.
    /// </summary>
    public int Value { get; set; }
}
