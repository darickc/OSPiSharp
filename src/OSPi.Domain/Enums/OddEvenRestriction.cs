namespace OSPi.Domain.Enums;

/// <summary>
/// Optional odd/even day restriction layered on top of the schedule.
/// Mirrors the firmware <c>ProgramStruct.oddeven</c> field.
/// </summary>
public enum OddEvenRestriction
{
    /// <summary>No restriction.</summary>
    None = 0,

    /// <summary>Run only on odd days of the month (excluding the 31st and Feb 29th).</summary>
    OddDays = 1,

    /// <summary>Run only on even days of the month.</summary>
    EvenDays = 2,
}
