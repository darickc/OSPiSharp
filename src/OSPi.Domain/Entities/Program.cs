using OSPi.Domain.Enums;

namespace OSPi.Domain.Entities;

/// <summary>
/// A watering program (firmware <c>ProgramStruct</c>) — what runs, when, and for how
/// long. Modernized for a fixed 16-zone clone: durations are first-class join rows
/// rather than a fixed-size array.
/// </summary>
public sealed class Program
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>User-facing program name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the program participates in scheduling.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether weather-based water-level scaling applies to this program.</summary>
    public bool UseWeather { get; set; } = true;

    /// <summary>Optional odd/even day restriction.</summary>
    public OddEvenRestriction OddEven { get; set; } = OddEvenRestriction.None;

    /// <summary>Which scheduling scheme determines the run days.</summary>
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Weekly;

    /// <summary>Weekly schedule: bit 0 = Monday .. bit 6 = Sunday.</summary>
    public byte WeekdayMask { get; set; }

    /// <summary>Interval schedule: number of days between runs.</summary>
    public int IntervalDays { get; set; }

    /// <summary>Interval schedule: <c>epochDay % IntervalDays == IntervalRemainder</c> triggers a run.</summary>
    public int IntervalRemainder { get; set; }

    /// <summary>Monthly schedule: day of month (1..31, or 32 = last day of month).</summary>
    public int MonthlyDay { get; set; }

    /// <summary>Single-run schedule: the one date the program runs.</summary>
    public DateOnly? SingleRunDate { get; set; }

    /// <summary>Whether start times are explicit (<see cref="StartTimeType.Fixed"/>) or repeating.</summary>
    public StartTimeType StartTimeType { get; set; } = StartTimeType.Fixed;

    /// <summary>Repeating mode: how many additional times to repeat after the anchor start.</summary>
    public int RepeatCount { get; set; }

    /// <summary>Repeating mode: minutes between repeats.</summary>
    public int RepeatEveryMinutes { get; set; }

    /// <summary>Whether the date-range gate is active.</summary>
    public bool DateRangeEnabled { get; set; }

    /// <summary>Date-range gate: start month (1..12).</summary>
    public int DateRangeStartMonth { get; set; }

    /// <summary>Date-range gate: start day (1..31).</summary>
    public int DateRangeStartDay { get; set; }

    /// <summary>Date-range gate: end month (1..12).</summary>
    public int DateRangeEndMonth { get; set; }

    /// <summary>Date-range gate: end day (1..31).</summary>
    public int DateRangeEndDay { get; set; }

    /// <summary>Up to four start times (owned). Slot 0 is the anchor in repeating mode.</summary>
    public List<ProgramStartTime> StartTimes { get; set; } = new();

    /// <summary>Per-zone durations and run order for this program.</summary>
    public ICollection<ProgramZoneDuration> ZoneDurations { get; set; } = new List<ProgramZoneDuration>();
}
