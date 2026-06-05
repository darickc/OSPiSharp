using FluentAssertions;
using OSPi.Web.Scheduling;

namespace OSPi.Web.Tests;

/// <summary>
/// The Scheduler grid lays zones back-to-back from a program's start; a run that crosses midnight
/// must continue into the next day's column. <see cref="ScheduleLayout.SplitAcrossDays"/> is the
/// pure midnight-splitting it relies on, exercised here against the bug-prone boundary cases.
/// </summary>
public class ScheduleLayoutTests
{
    private const int Day = 86_400;

    [Fact]
    public void Within_one_day_yields_a_single_uncontinued_segment()
    {
        // 11:00 PM – midnight.
        var segs = ScheduleLayout.SplitAcrossDays(23 * 3600, 24 * 3600).ToList();

        segs.Should().ContainSingle();
        segs[0].Should().Be(new DaySegment(DayOffset: 0, WithinStartSec: 23 * 3600, WithinEndSec: Day, Continued: false));
    }

    [Fact]
    public void Crossing_midnight_splits_into_two_segments_and_marks_the_second_continued()
    {
        // 11:30 PM – 12:30 AM (one hour, straddling midnight).
        int start = 23 * 3600 + 1800;
        int end = start + 3600;
        var segs = ScheduleLayout.SplitAcrossDays(start, end).ToList();

        segs.Should().HaveCount(2);
        segs[0].Should().Be(new DaySegment(0, start, Day, Continued: false));
        segs[1].Should().Be(new DaySegment(1, 0, 1800, Continued: true));

        // The slices reconstruct the original duration.
        int total = segs.Sum(s => s.WithinEndSec - s.WithinStartSec);
        total.Should().Be(end - start);
    }

    [Fact]
    public void A_zone_starting_exactly_at_midnight_is_a_fresh_run_not_a_continuation()
    {
        // 12:00 AM – 1:00 AM next day (e.g. zone 2 of an 11 PM program after zone 1 fills the prior hour).
        var segs = ScheduleLayout.SplitAcrossDays(Day, Day + 3600).ToList();

        segs.Should().ContainSingle();
        segs[0].Should().Be(new DaySegment(DayOffset: 1, WithinStartSec: 0, WithinEndSec: 3600, Continued: false));
    }

    [Fact]
    public void Eleven_pm_three_hour_run_lands_zone_blocks_on_both_days()
    {
        // The reported case: start 11 PM, three 60-min zones laid back-to-back => 11 PM–2 AM.
        var segs = ScheduleLayout.SplitAcrossDays(23 * 3600, 23 * 3600 + 3 * 3600).ToList();

        segs.Should().HaveCount(2);
        segs[0].Should().Be(new DaySegment(0, 23 * 3600, Day, Continued: false));      // 11 PM–midnight
        segs[1].Should().Be(new DaySegment(1, 0, 2 * 3600, Continued: true));          // midnight–2 AM next day
    }

    [Fact]
    public void Spanning_multiple_midnights_yields_a_segment_per_day()
    {
        // 26-hour run (degenerate, but the loop must be general): touches 3 calendar days.
        var segs = ScheduleLayout.SplitAcrossDays(23 * 3600, 23 * 3600 + 26 * 3600).ToList();

        segs.Should().HaveCount(3);
        segs[0].DayOffset.Should().Be(0);
        segs[1].Should().Be(new DaySegment(1, 0, Day, Continued: true));
        segs[2].DayOffset.Should().Be(2);
        segs.Skip(1).Should().OnlyContain(s => s.Continued);
    }

    [Fact]
    public void Empty_or_inverted_window_yields_nothing()
    {
        ScheduleLayout.SplitAcrossDays(3600, 3600).Should().BeEmpty();
        ScheduleLayout.SplitAcrossDays(7200, 3600).Should().BeEmpty();
    }
}
