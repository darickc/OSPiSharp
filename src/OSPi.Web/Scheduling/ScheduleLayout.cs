namespace OSPi.Web.Scheduling;

/// <summary>
/// One within-a-day slice of a zone run. A run that crosses midnight produces several of these,
/// one per calendar day it touches, so the Scheduler can place each slice in the right column.
/// </summary>
/// <param name="DayOffset">Whole days after the run's own start day (0 = the start day).</param>
/// <param name="WithinStartSec">Start second within that day (0..86400).</param>
/// <param name="WithinEndSec">End second within that day (0..86400).</param>
/// <param name="Continued">True when this slice continues a zone that began on an earlier day.</param>
public readonly record struct DaySegment(int DayOffset, int WithinStartSec, int WithinEndSec, bool Continued);

/// <summary>Pure layout helpers for the weekly Scheduler grid — no I/O, easily unit-tested.</summary>
public static class ScheduleLayout
{
    private const int SecondsPerDay = 86_400;

    /// <summary>
    /// Split a run window, expressed as seconds from its start day's midnight, into per-calendar-day
    /// segments. A run wholly within one day yields a single segment; a run crossing midnight yields
    /// one segment per day it touches, with every segment after the first flagged
    /// <see cref="DaySegment.Continued"/>. A run that merely *starts* exactly at midnight is a fresh
    /// run on that day, not a continuation.
    /// </summary>
    public static IEnumerable<DaySegment> SplitAcrossDays(int absStartSec, int absEndSec)
    {
        if (absEndSec <= absStartSec) yield break;

        int segStart = absStartSec;
        while (segStart < absEndSec)
        {
            int dayOffset = segStart / SecondsPerDay;
            int dayBase = dayOffset * SecondsPerDay;
            int segEnd = Math.Min(absEndSec, dayBase + SecondsPerDay);

            yield return new DaySegment(
                DayOffset: dayOffset,
                WithinStartSec: segStart - dayBase,
                WithinEndSec: segEnd - dayBase,
                Continued: segStart > absStartSec);

            segStart = segEnd;
        }
    }
}
