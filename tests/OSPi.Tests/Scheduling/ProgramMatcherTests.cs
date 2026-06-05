using FluentAssertions;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;
using OSPi.Domain.Scheduling;

namespace OSPi.Tests.Scheduling;

/// <summary>
/// Day/time matching tests. Each test cites the firmware line it encodes so the port can be
/// verified against the oracle (program.cpp). All times are site-local civil time.
/// </summary>
public class ProgramMatcherTests
{
    private const int NoSun = 0; // sunrise/sunset not used by fixed-minute tests

    private static CivilInstant At(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(new DateTime(year, month, day, hour, minute, 0));

    private static Program Weekly(byte mask) => new()
    {
        Enabled = true,
        ScheduleType = ScheduleType.Weekly,
        WeekdayMask = mask,
        StartTimeType = StartTimeType.Fixed,
    };

    // --- 1: weekday -> bit mapping (Mon=bit0 .. Sun=bit6), program.cpp:245,263 ---
    [Theory]
    [InlineData("2024-01-01", 0)] // Monday
    [InlineData("2024-01-02", 1)] // Tuesday
    [InlineData("2024-01-03", 2)] // Wednesday
    [InlineData("2024-01-04", 3)] // Thursday
    [InlineData("2024-01-05", 4)] // Friday
    [InlineData("2024-01-06", 5)] // Saturday
    [InlineData("2024-01-07", 6)] // Sunday
    public void Weekly_maps_each_weekday_to_the_correct_bit(string date, int bit)
    {
        var d = DateOnly.Parse(date);
        var when = At(d.Year, d.Month, d.Day);
        var p = Weekly((byte)(1 << bit));

        ProgramMatcher.CheckDayMatch(p, when).Should().BeTrue();
        // Any other single-bit mask should reject this day.
        var other = Weekly((byte)(1 << ((bit + 1) % 7)));
        ProgramMatcher.CheckDayMatch(other, when).Should().BeFalse();
    }

    // --- 2: non-selected weekday rejects, program.cpp:263 ---
    [Fact]
    public void Weekly_rejects_a_day_not_in_the_mask()
    {
        var monOnly = Weekly(0b0000001); // Monday only
        ProgramMatcher.CheckDayMatch(monOnly, At(2024, 1, 2)).Should().BeFalse(); // Tuesday
    }

    // --- 3: SingleRun matches only the exact date, program.cpp:267-271 ---
    [Fact]
    public void SingleRun_matches_only_its_date()
    {
        var p = new Program
        {
            Enabled = true,
            ScheduleType = ScheduleType.SingleRun,
            SingleRunDate = new DateOnly(2024, 6, 15),
        };

        ProgramMatcher.CheckDayMatch(p, At(2024, 6, 15)).Should().BeTrue();
        ProgramMatcher.CheckDayMatch(p, At(2024, 6, 14)).Should().BeFalse();
        ProgramMatcher.CheckDayMatch(p, At(2024, 6, 16)).Should().BeFalse();
    }

    // --- 4: Monthly fixed day, program.cpp:283 ---
    [Fact]
    public void Monthly_matches_the_configured_day()
    {
        var p = new Program { Enabled = true, ScheduleType = ScheduleType.Monthly, MonthlyDay = 15 };
        ProgramMatcher.CheckDayMatch(p, At(2024, 6, 15)).Should().BeTrue();
        ProgramMatcher.CheckDayMatch(p, At(2024, 6, 14)).Should().BeFalse();
    }

    // --- 5: Monthly last-day across months and leap/non-leap Feb, program.cpp:274-282 ---
    [Theory]
    [InlineData(2024, 1, 31, true)]   // Jan -> 31
    [InlineData(2024, 1, 30, false)]
    [InlineData(2024, 4, 30, true)]   // Apr -> 30
    [InlineData(2024, 4, 29, false)]
    [InlineData(2024, 2, 29, true)]   // Feb leap -> 29
    [InlineData(2024, 2, 28, false)]
    [InlineData(2023, 2, 28, true)]   // Feb non-leap -> 28
    [InlineData(2023, 2, 27, false)]
    public void Monthly_last_day_handles_month_lengths_and_leap_years(int y, int m, int d, bool expected)
    {
        var p = new Program { Enabled = true, ScheduleType = ScheduleType.Monthly, MonthlyDay = 32 };
        ProgramMatcher.CheckDayMatch(p, At(y, m, d)).Should().Be(expected);
    }

    // --- 6: Interval modulus with rollover, program.cpp:291 ---
    [Fact]
    public void Interval_matches_on_remainder_and_rolls_over()
    {
        // 2024-01-01 is Unix epoch day 19723; 19723 % 3 == 1.
        var p = new Program { Enabled = true, ScheduleType = ScheduleType.Interval, IntervalDays = 3, IntervalRemainder = 1 };
        ProgramMatcher.CheckDayMatch(p, At(2024, 1, 1)).Should().BeTrue();  // rem 1
        ProgramMatcher.CheckDayMatch(p, At(2024, 1, 2)).Should().BeFalse(); // rem 2
        ProgramMatcher.CheckDayMatch(p, At(2024, 1, 3)).Should().BeFalse(); // rem 0
        ProgramMatcher.CheckDayMatch(p, At(2024, 1, 4)).Should().BeTrue();  // rem 1 again
    }

    // --- 7: Odd-day restriction skips the 31st and Feb 29, program.cpp:303-305 ---
    [Theory]
    [InlineData(2024, 1, 15, true)]  // odd
    [InlineData(2024, 1, 16, false)] // even
    [InlineData(2024, 1, 31, false)] // 31st skipped
    [InlineData(2024, 2, 29, false)] // Feb 29 skipped
    public void Odd_days_restriction(int y, int m, int d, bool expected)
    {
        var p = Weekly(0b1111111); // all weekdays, so only odd/even decides
        p.OddEven = OddEvenRestriction.OddDays;
        ProgramMatcher.CheckDayMatch(p, At(y, m, d)).Should().Be(expected);
    }

    // --- 8: Even-day restriction, program.cpp:299 ---
    [Theory]
    [InlineData(2024, 1, 16, true)]  // even
    [InlineData(2024, 1, 15, false)] // odd
    public void Even_days_restriction(int y, int m, int d, bool expected)
    {
        var p = Weekly(0b1111111);
        p.OddEven = OddEvenRestriction.EvenDays;
        ProgramMatcher.CheckDayMatch(p, At(y, m, d)).Should().Be(expected);
    }

    // --- 9: Odd/even is a SECOND gate layered on the schedule, program.cpp:295 ---
    [Fact]
    public void Odd_even_gate_can_reject_a_weekday_match()
    {
        // 2024-01-16 is a Tuesday (even day).
        var tueEven = Weekly(0b0000010); // Tuesday
        ProgramMatcher.CheckDayMatch(tueEven, At(2024, 1, 16)).Should().BeTrue();

        tueEven.OddEven = OddEvenRestriction.OddDays;
        ProgramMatcher.CheckDayMatch(tueEven, At(2024, 1, 16)).Should().BeFalse();
    }

    // --- 10: Date range without year-wrap, program.cpp:251-252 ---
    [Fact]
    public void Date_range_no_wrap_gates_the_season()
    {
        var p = Weekly(0b1111111);
        p.DateRangeEnabled = true;
        p.DateRangeStartMonth = 4; p.DateRangeStartDay = 1;
        p.DateRangeEndMonth = 9; p.DateRangeEndDay = 30;

        ProgramMatcher.CheckDayMatch(p, At(2024, 6, 15)).Should().BeTrue();
        ProgramMatcher.CheckDayMatch(p, At(2024, 2, 1)).Should().BeFalse();
        ProgramMatcher.CheckDayMatch(p, At(2024, 11, 1)).Should().BeFalse();
        ProgramMatcher.CheckDayMatch(p, At(2024, 4, 1)).Should().BeTrue();   // inclusive start
        ProgramMatcher.CheckDayMatch(p, At(2024, 9, 30)).Should().BeTrue();  // inclusive end
    }

    // --- 11: Date range that wraps the year end, program.cpp:253-255 ---
    [Fact]
    public void Date_range_year_wrap_gates_the_season()
    {
        var p = Weekly(0b1111111);
        p.DateRangeEnabled = true;
        p.DateRangeStartMonth = 11; p.DateRangeStartDay = 1;
        p.DateRangeEndMonth = 2; p.DateRangeEndDay = 28;

        ProgramMatcher.CheckDayMatch(p, At(2024, 12, 15)).Should().BeTrue();
        ProgramMatcher.CheckDayMatch(p, At(2024, 1, 15)).Should().BeTrue();
        ProgramMatcher.CheckDayMatch(p, At(2024, 7, 15)).Should().BeFalse();
        ProgramMatcher.CheckDayMatch(p, At(2024, 11, 1)).Should().BeTrue();   // inclusive start
        ProgramMatcher.CheckDayMatch(p, At(2024, 2, 28)).Should().BeTrue();   // inclusive end
    }

    // --- 12: Fixed start times return the 1-based slot index, program.cpp:337-345 ---
    [Fact]
    public void Fixed_start_times_return_slot_index_plus_one()
    {
        var p = Weekly(0b1111111);
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 360 }); // 06:00
        p.StartTimes.Add(new ProgramStartTime { Slot = 1, Kind = StartTimeKind.FixedMinute, Value = 420 }); // 07:00

        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 6, 0), NoSun, NoSun).RunIndex.Should().Be(1);
        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 7, 0), NoSun, NoSun).RunIndex.Should().Be(2);
        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 6, 30), NoSun, NoSun).Matched.Should().BeFalse();
    }

    // --- 13: Fixed + SingleRun deletes only on the max (last) start time, program.cpp:340-341 ---
    [Fact]
    public void Fixed_single_run_deletes_only_on_the_last_start_time()
    {
        var p = new Program
        {
            Enabled = true,
            ScheduleType = ScheduleType.SingleRun,
            SingleRunDate = new DateOnly(2024, 1, 1),
            StartTimeType = StartTimeType.Fixed,
        };
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 360 });
        p.StartTimes.Add(new ProgramStartTime { Slot = 1, Kind = StartTimeKind.FixedMinute, Value = 420 });

        var early = ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 6, 0), NoSun, NoSun);
        early.RunIndex.Should().Be(1);
        early.ShouldDelete.Should().BeFalse();

        var last = ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 7, 0), NoSun, NoSun);
        last.RunIndex.Should().Be(2);
        last.ShouldDelete.Should().BeTrue();
    }

    // --- 14: Repeating anchor returns run index 1; single-run no-interval deletes, program.cpp:353-359 ---
    [Fact]
    public void Repeating_anchor_returns_one_and_single_run_no_interval_deletes()
    {
        var weekly = Weekly(0b1111111);
        weekly.StartTimeType = StartTimeType.Repeating;
        weekly.StartTimes.Add(new ProgramStartTime { Slot = 0, Value = 360 });
        var m = ProgramMatcher.CheckMatch(weekly, At(2024, 1, 1, 6, 0), NoSun, NoSun);
        m.RunIndex.Should().Be(1);
        m.ShouldDelete.Should().BeFalse();

        var single = new Program
        {
            Enabled = true,
            ScheduleType = ScheduleType.SingleRun,
            SingleRunDate = new DateOnly(2024, 1, 1),
            StartTimeType = StartTimeType.Repeating,
            RepeatEveryMinutes = 0,
        };
        single.StartTimes.Add(new ProgramStartTime { Slot = 0, Value = 360 });
        ProgramMatcher.CheckMatch(single, At(2024, 1, 1, 6, 0), NoSun, NoSun).ShouldDelete.Should().BeTrue();
    }

    // --- 15: Repeating interval matches exact multiples up to repeat, program.cpp:363-374 ---
    [Fact]
    public void Repeating_interval_matches_exact_multiples_within_repeat_count()
    {
        var p = Weekly(0b1111111);
        p.StartTimeType = StartTimeType.Repeating;
        p.RepeatEveryMinutes = 30;
        p.RepeatCount = 3;
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Value = 360 }); // 06:00

        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 6, 0), NoSun, NoSun).RunIndex.Should().Be(1);  // c=0
        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 6, 30), NoSun, NoSun).RunIndex.Should().Be(2); // c=1
        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 7, 30), NoSun, NoSun).RunIndex.Should().Be(4); // c=3 (== repeat)
        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 8, 0), NoSun, NoSun).Matched.Should().BeFalse(); // c=4 > repeat
        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 6, 15), NoSun, NoSun).Matched.Should().BeFalse(); // not a multiple
    }

    // --- 16: Repeating single-run deletes when c == repeat, program.cpp:368 ---
    [Fact]
    public void Repeating_single_run_deletes_on_final_repeat()
    {
        var p = new Program
        {
            Enabled = true,
            ScheduleType = ScheduleType.SingleRun,
            SingleRunDate = new DateOnly(2024, 1, 1),
            StartTimeType = StartTimeType.Repeating,
            RepeatEveryMinutes = 30,
            RepeatCount = 2,
        };
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Value = 360 });

        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 6, 0), NoSun, NoSun).ShouldDelete.Should().BeFalse();  // c=0
        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 6, 30), NoSun, NoSun).ShouldDelete.Should().BeFalse(); // c=1
        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 7, 0), NoSun, NoSun).ShouldDelete.Should().BeTrue();   // c=2 == repeat
    }

    // --- 17: Overnight match via the previous day, program.cpp:382-393 ---
    [Fact]
    public void Overnight_run_matches_against_the_previous_day()
    {
        var p = Weekly(0b0000001); // Monday only
        p.StartTimeType = StartTimeType.Repeating;
        p.RepeatEveryMinutes = 10;
        p.RepeatCount = 5;
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Value = 1430 }); // 23:50 Monday

        // 2024-01-02 is Tuesday; today's day-match fails, but Monday (the previous civil day) matches.
        var m = ProgramMatcher.CheckMatch(p, At(2024, 1, 2, 0, 10), NoSun, NoSun); // 00:10
        m.RunIndex.Should().Be(3); // c = (10 - 1430 + 1440) / 10 = 2
    }

    // --- 18: Fixed start type never matches overnight, program.cpp:379 ---
    [Fact]
    public void Fixed_start_type_does_not_match_overnight()
    {
        var p = Weekly(0b0000001); // Monday only, Fixed
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 1430 });

        ProgramMatcher.CheckMatch(p, At(2024, 1, 2, 0, 10), NoSun, NoSun).Matched.Should().BeFalse();
    }

    // --- 19: Sunrise offset clamps only at the low end, program.cpp:218-219 ---
    [Fact]
    public void Sunrise_offset_clamps_low_only()
    {
        var early = new ProgramStartTime { Kind = StartTimeKind.SunriseOffset, Value = -400 };
        ProgramMatcher.ResolveStartMinute(early, sunriseMinute: 300, sunsetMinute: 1200).Should().Be(0);

        // No high clamp: a large positive offset can exceed 1439.
        var late = new ProgramStartTime { Kind = StartTimeKind.SunriseOffset, Value = 2000 };
        ProgramMatcher.ResolveStartMinute(late, sunriseMinute: 300, sunsetMinute: 1200).Should().Be(2300);
    }

    // --- 20: Sunset offset clamps only at the high end, program.cpp:220-222 ---
    [Fact]
    public void Sunset_offset_clamps_high_only()
    {
        var late = new ProgramStartTime { Kind = StartTimeKind.SunsetOffset, Value = 400 };
        ProgramMatcher.ResolveStartMinute(late, sunriseMinute: 300, sunsetMinute: 1200).Should().Be(1439);

        // No low clamp: a large negative offset can go below 0 (faithful asymmetry).
        var early = new ProgramStartTime { Kind = StartTimeKind.SunsetOffset, Value = -500 };
        ProgramMatcher.ResolveStartMinute(early, sunriseMinute: 300, sunsetMinute: 100).Should().Be(-400);
    }

    // --- 21: A disabled program never matches, program.cpp:318 ---
    [Fact]
    public void Disabled_program_never_matches()
    {
        var p = Weekly(0b1111111);
        p.Enabled = false;
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 360 });

        ProgramMatcher.CheckMatch(p, At(2024, 1, 1, 6, 0), NoSun, NoSun).Matched.Should().BeFalse();
    }

    // ===== StartMinutesOn: the scheduler-view day projection =====

    // --- 22: Fixed mode returns every present slot, ordered and de-duplicated ---
    [Fact]
    public void StartMinutesOn_fixed_returns_all_slots_sorted()
    {
        var p = Weekly(0b1111111);
        p.StartTimes.Add(new ProgramStartTime { Slot = 1, Kind = StartTimeKind.FixedMinute, Value = 420 }); // 07:00
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 360 }); // 06:00
        p.StartTimes.Add(new ProgramStartTime { Slot = 2, Kind = StartTimeKind.FixedMinute, Value = 360 }); // dup 06:00

        ProgramMatcher.StartMinutesOn(p, At(2024, 1, 1), NoSun, NoSun).Should().Equal(360, 420);
    }

    // --- 23: A day the schedule does not match yields nothing ---
    [Fact]
    public void StartMinutesOn_empty_when_day_does_not_match()
    {
        var monOnly = Weekly(0b0000001); // Monday only
        monOnly.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 360 });

        ProgramMatcher.StartMinutesOn(monOnly, At(2024, 1, 2), NoSun, NoSun).Should().BeEmpty(); // Tuesday
    }

    // --- 24: Repeating expands anchor + k*interval, same calendar day only ---
    [Fact]
    public void StartMinutesOn_repeating_expands_within_the_day()
    {
        var p = Weekly(0b1111111);
        p.StartTimeType = StartTimeType.Repeating;
        p.RepeatEveryMinutes = 30;
        p.RepeatCount = 3;
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Value = 360 }); // 06:00

        ProgramMatcher.StartMinutesOn(p, At(2024, 1, 1), NoSun, NoSun)
            .Should().Equal(360, 390, 420, 450); // 06:00, 06:30, 07:00, 07:30

        // Repeats that cross midnight are dropped (they belong to the next day's column).
        p.StartTimes.Clear();
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Value = 1430 }); // 23:50
        ProgramMatcher.StartMinutesOn(p, At(2024, 1, 1), NoSun, NoSun).Should().Equal(1430);
    }

    // --- 25: Sunrise/sunset slots resolve via the supplied sun minutes ---
    [Fact]
    public void StartMinutesOn_resolves_sunrise_and_sunset_slots()
    {
        var p = Weekly(0b1111111);
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.SunriseOffset, Value = -15 });
        p.StartTimes.Add(new ProgramStartTime { Slot = 1, Kind = StartTimeKind.SunsetOffset, Value = 0 });

        ProgramMatcher.StartMinutesOn(p, At(2024, 6, 1), sunriseMinute: 360, sunsetMinute: 1200)
            .Should().Equal(345, 1200);
    }

    // --- 26: Single-run only on its date; Interval honors its modulus ---
    [Fact]
    public void StartMinutesOn_respects_single_run_and_interval_days()
    {
        var single = new Program
        {
            Enabled = true,
            ScheduleType = ScheduleType.SingleRun,
            SingleRunDate = new DateOnly(2024, 6, 15),
            StartTimeType = StartTimeType.Fixed,
        };
        single.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 480 });
        ProgramMatcher.StartMinutesOn(single, At(2024, 6, 15), NoSun, NoSun).Should().Equal(480);
        ProgramMatcher.StartMinutesOn(single, At(2024, 6, 16), NoSun, NoSun).Should().BeEmpty();

        var interval = new Program
        {
            Enabled = true,
            ScheduleType = ScheduleType.Interval,
            IntervalDays = 3,
            IntervalRemainder = 1, // matches 2024-01-01 (epoch day % 3 == 1)
            StartTimeType = StartTimeType.Fixed,
        };
        interval.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 300 });
        ProgramMatcher.StartMinutesOn(interval, At(2024, 1, 1), NoSun, NoSun).Should().Equal(300);
        ProgramMatcher.StartMinutesOn(interval, At(2024, 1, 2), NoSun, NoSun).Should().BeEmpty();
    }

    // --- 27: Disabled program projects nothing ---
    [Fact]
    public void StartMinutesOn_empty_when_disabled()
    {
        var p = Weekly(0b1111111);
        p.Enabled = false;
        p.StartTimes.Add(new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 360 });

        ProgramMatcher.StartMinutesOn(p, At(2024, 1, 1), NoSun, NoSun).Should().BeEmpty();
    }
}
