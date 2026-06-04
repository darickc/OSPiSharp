using OSPi.Domain.Entities;
using OSPi.Domain.Enums;

namespace OSPi.Domain.Scheduling;

/// <summary>
/// The outcome of matching a program against a moment in time.
/// </summary>
/// <param name="RunIndex">
/// 1-based run count for this match (the firmware's <c>check_match</c> return value):
/// the n-th start of the day. 0 means no match.
/// </param>
/// <param name="ShouldDelete">
/// True when this match is the program's final run and the program is a single-run, so
/// the caller should retire it (firmware <c>to_delete</c>).
/// </param>
public readonly record struct MatchResult(int RunIndex, bool ShouldDelete)
{
    /// <summary>No match.</summary>
    public static readonly MatchResult None = new(0, false);

    /// <summary>Whether the program runs at this moment.</summary>
    public bool Matched => RunIndex > 0;
}

/// <summary>
/// Pure port of the firmware's program day/time matching. No I/O, no clock, no EF.
///
/// <para>Oracle: <c>program.cpp</c> — <c>starttime_decode</c> (213-225),
/// <c>check_day_match</c> (228-308), <c>check_match</c> (315-396). All matching runs on
/// <see cref="CivilInstant"/> (site-local civil time), faithfully to the firmware.</para>
/// </summary>
public static class ProgramMatcher
{
    /// <summary>
    /// Resolve a start time to an absolute minute-of-day, applying sunrise/sunset offsets.
    /// Mirrors <c>starttime_decode</c> (program.cpp:213-225), including the clamp
    /// <em>asymmetry</em>: a sunrise result is clamped only at the low end (>= 0), a sunset
    /// result only at the high end (&lt;= 1439).
    /// </summary>
    public static int ResolveStartMinute(ProgramStartTime st, int sunriseMinute, int sunsetMinute) => st.Kind switch
    {
        StartTimeKind.SunriseOffset => Math.Max(0, sunriseMinute + st.Value),     // program.cpp:218-219
        StartTimeKind.SunsetOffset => Math.Min(1439, sunsetMinute + st.Value),    // program.cpp:220-222
        _ => st.Value,                                                            // FixedMinute
    };

    /// <summary>
    /// Whether <paramref name="when"/> falls on a day the program is scheduled to run,
    /// considering the date-range gate, schedule type, and odd/even restriction. Mirrors
    /// <c>check_day_match</c> (program.cpp:228-308).
    /// </summary>
    public static bool CheckDayMatch(Program p, CivilInstant when)
    {
        var dt = when.LocalDateTime;
        int month = dt.Month, day = dt.Day, year = dt.Year;

        // Date-range gate, with year-wrap handling (program.cpp:248-257). The encoding only
        // needs to be monotonic in (month, day) for the comparisons to order correctly.
        if (p.DateRangeEnabled)
        {
            int curr = Encode(month, day);
            int lo = Encode(p.DateRangeStartMonth, p.DateRangeStartDay);
            int hi = Encode(p.DateRangeEndMonth, p.DateRangeEndDay);
            if (lo <= hi)
            {
                if (curr < lo || curr > hi) return false;
            }
            else if (curr > hi && curr < lo)
            {
                return false; // range crosses year-end; reject only the gap between hi and lo
            }
        }

        switch (p.ScheduleType)
        {
            case ScheduleType.Weekly:
                // Firmware wd = (weekday+5)%7 yields Mon=0..Sun=6; WeekdayMask bit0=Mon.
                // .NET DayOfWeek has Sunday=0, so (DayOfWeek+6)%7 gives the same Mon=0..Sun=6.
                int wd = ((int)when.DayOfWeek + 6) % 7;            // program.cpp:245
                if ((p.WeekdayMask & (1 << wd)) == 0) return false; // program.cpp:263
                break;

            case ScheduleType.SingleRun:                           // program.cpp:267-271
                if (p.SingleRunDate is null || when.Date != p.SingleRunDate.Value) return false;
                break;

            case ScheduleType.Monthly:                             // program.cpp:273-287
                // MonthlyDay 32 == "last day of month"; DaysInMonth already handles Feb leap years.
                if (p.MonthlyDay == 32)
                {
                    if (day != DateTime.DaysInMonth(year, month)) return false;
                }
                else if (day != p.MonthlyDay)
                {
                    return false;
                }
                break;

            case ScheduleType.Interval:                            // program.cpp:289-292
                if (p.IntervalDays <= 0) return false;             // guard div-by-zero (firmware data is always >0)
                if (when.EpochDay % p.IntervalDays != p.IntervalRemainder) return false;
                break;
        }

        // Odd/even gate, applied after the type switch (program.cpp:295-306).
        switch (p.OddEven)
        {
            case OddEvenRestriction.EvenDays:
                if (day % 2 != 0) return false;
                break;
            case OddEvenRestriction.OddDays:
                if (day == 31) return false;            // skip the 31st
                if (day == 29 && month == 2) return false; // skip Feb 29
                if (day % 2 != 1) return false;
                break;
        }

        return true;
    }

    /// <summary>
    /// Whether the program starts at <paramref name="when"/>, also catching programs that
    /// started the previous day and ran overnight. Mirrors <c>check_match</c>
    /// (program.cpp:315-396).
    /// </summary>
    public static MatchResult CheckMatch(Program p, CivilInstant when, int sunriseMinute, int sunsetMinute)
    {
        if (!p.Enabled) return MatchResult.None;                   // program.cpp:318

        var anchor = FindAnchor(p);
        int start = anchor is null ? -1 : ResolveStartMinute(anchor, sunriseMinute, sunsetMinute);
        int repeat = p.RepeatCount;
        int interval = p.RepeatEveryMinutes;
        int currentMinute = when.MinuteOfDay;                      // program.cpp:323
        bool singleRun = p.ScheduleType == ScheduleType.SingleRun;

        // Assume the program starts today.
        if (CheckDayMatch(p, when))
        {
            if (p.StartTimeType == StartTimeType.Fixed)            // program.cpp:329-348
            {
                // Max over the present slots' resolved minutes. NOTE: the firmware uses an
                // `unsigned char` accumulator here (program.cpp:331), which truncates start
                // times above 255 minutes — a bug that only affects the single-run delete
                // decision. We deliberately compute the correct max instead.
                int maxStart = int.MinValue;
                foreach (var st in p.StartTimes)
                {
                    int m = ResolveStartMinute(st, sunriseMinute, sunsetMinute);
                    if (m > maxStart) maxStart = m;
                }

                foreach (var st in OrderedBySlot(p.StartTimes))
                {
                    if (currentMinute == ResolveStartMinute(st, sunriseMinute, sunsetMinute))
                    {
                        bool del = singleRun && maxStart == currentMinute;
                        return new MatchResult(st.Slot + 1, del); // firmware returns array index i+1
                    }
                }
                return MatchResult.None;
            }

            // Repeating type (program.cpp:349-376).
            if (anchor is not null)
            {
                if (currentMinute == start)
                {
                    bool del = singleRun && interval == 0;
                    return new MatchResult(1, del);
                }

                if (currentMinute > start && interval != 0)
                {
                    int c = (currentMinute - start) / interval;
                    if (c * interval == currentMinute - start && c <= repeat)
                    {
                        bool del = singleRun && c == repeat;
                        return new MatchResult(c + 1, del);
                    }
                }
            }
        }

        // To reach the overnight branch the program must be repeating with a non-zero
        // interval (program.cpp:379).
        if (p.StartTimeType == StartTimeType.Fixed || interval == 0 || anchor is null)
            return MatchResult.None;

        // Assume the program started the previous day and ran over night (program.cpp:382-394).
        if (CheckDayMatch(p, when.PreviousDay()))
        {
            int c = (currentMinute - start + 1440) / interval;
            if (c * interval == currentMinute - start + 1440 && c <= repeat)
            {
                bool del = singleRun && c == repeat;
                return new MatchResult(c + 1, del);
            }
        }

        return MatchResult.None;
    }

    /// <summary>Date encoding monotonic in (month, day), mirroring <c>date_encode</c>.</summary>
    private static int Encode(int month, int day) => (month << 5) | day;

    /// <summary>The repeating-mode anchor (slot 0), or null if absent.</summary>
    private static ProgramStartTime? FindAnchor(Program p)
    {
        foreach (var st in p.StartTimes)
            if (st.Slot == 0) return st;
        return null;
    }

    private static IEnumerable<ProgramStartTime> OrderedBySlot(IEnumerable<ProgramStartTime> startTimes)
    {
        var list = new List<ProgramStartTime>(startTimes);
        list.Sort(static (a, b) => a.Slot.CompareTo(b.Slot));
        return list;
    }
}
