namespace OSPi.Domain.Enums;

/// <summary>
/// How a program's run days are determined. Mirrors the firmware
/// <c>ProgramStruct.type</c> values, so the numeric values are significant.
/// </summary>
public enum ScheduleType
{
    /// <summary>Runs on a weekly weekday bitmask (<see cref="Entities.Program.WeekdayMask"/>).</summary>
    Weekly = 0,

    /// <summary>Runs exactly once on a specific date (<see cref="Entities.Program.SingleRunDate"/>).</summary>
    SingleRun = 1,

    /// <summary>Runs on a day of the month (<see cref="Entities.Program.MonthlyDay"/>).</summary>
    Monthly = 2,

    /// <summary>Runs every N days (<see cref="Entities.Program.IntervalDays"/>).</summary>
    Interval = 3,
}
