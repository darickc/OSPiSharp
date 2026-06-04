using System.Collections.Immutable;
using FluentAssertions;
using OSPi.Domain.Enums;
using OSPi.Domain.Scheduling;

namespace OSPi.Tests.Scheduling;

/// <summary>
/// Queue-planning tests for <see cref="StationScheduler"/>. Times are integer seconds
/// relative to the planning base (offset 0 = "now"). Each test cites the firmware line it
/// encodes (main.cpp / OpenSprinkler.cpp).
/// </summary>
public class StationSchedulerTests
{
    private static PendingZone Pending(int zoneId, ZoneGroup group, int duration) =>
        new(zoneId, HardwareBit: zoneId, group, duration, ProgramId: 1);

    private static PlanRequest Request(IEnumerable<PendingZone> newZones, int delay = 0, bool insertFront = false,
        IEnumerable<QueueItem>? existing = null, IEnumerable<MasterBinding>? masters = null) => new()
    {
        NewZones = newZones.ToImmutableArray(),
        ExistingItems = (existing ?? Array.Empty<QueueItem>()).ToImmutableArray(),
        StationDelaySeconds = delay,
        InsertFront = insertFront,
        Masters = (masters ?? Array.Empty<MasterBinding>()).ToImmutableArray(),
    };

    // --- 22: Sequential group runs one-at-a-time with station delay between, main.cpp:1576-1578 ---
    [Fact]
    public void Sequential_zones_run_back_to_back_with_station_delay()
    {
        var plan = StationScheduler.Plan(Request(
            new[] { Pending(1, ZoneGroup.Sequential0, 60), Pending(2, ZoneGroup.Sequential0, 120) },
            delay: 10));

        plan.Items.Should().HaveCount(2);
        plan.Items[0].StartOffsetSeconds.Should().Be(1);   // first sequential zone starts at +1 (stagger)
        plan.Items[0].DequeueOffsetSeconds.Should().Be(61);
        plan.Items[1].StartOffsetSeconds.Should().Be(71);  // 1 + 60 + 10 delay
        plan.Items[1].DequeueOffsetSeconds.Should().Be(191);
    }

    // --- 23: Station delay boundary (0 vs >0) ---
    [Fact]
    public void Station_delay_shifts_the_following_zone()
    {
        var noDelay = StationScheduler.Plan(Request(
            new[] { Pending(1, ZoneGroup.Sequential0, 60), Pending(2, ZoneGroup.Sequential0, 60) }, delay: 0));
        noDelay.Items[1].StartOffsetSeconds.Should().Be(61); // 1 + 60

        var withDelay = StationScheduler.Plan(Request(
            new[] { Pending(1, ZoneGroup.Sequential0, 60), Pending(2, ZoneGroup.Sequential0, 60) }, delay: 15));
        withDelay.Items[1].StartOffsetSeconds.Should().Be(76); // 1 + 60 + 15
    }

    // --- 24: Four sequential groups staggered 1s apart, main.cpp:1485-1495,1551 ---
    [Fact]
    public void Sequential_groups_are_staggered_one_second_apart()
    {
        var plan = StationScheduler.Plan(Request(new[]
        {
            Pending(1, ZoneGroup.Sequential0, 60),
            Pending(2, ZoneGroup.Sequential1, 60),
            Pending(3, ZoneGroup.Sequential2, 60),
            Pending(4, ZoneGroup.Sequential3, 60),
        }));

        plan.Items[0].StartOffsetSeconds.Should().Be(1);
        plan.Items[1].StartOffsetSeconds.Should().Be(2);
        plan.Items[2].StartOffsetSeconds.Should().Be(3);
        plan.Items[3].StartOffsetSeconds.Should().Be(4);
    }

    // --- 25: Parallel zones start concurrently, staggered 1s, main.cpp:1581-1583 ---
    [Fact]
    public void Parallel_zones_start_one_second_apart_from_the_concurrent_base()
    {
        var plan = StationScheduler.Plan(Request(new[]
        {
            Pending(1, ZoneGroup.Parallel, 60),
            Pending(2, ZoneGroup.Parallel, 60),
            Pending(3, ZoneGroup.Parallel, 60),
        }));

        plan.Items[0].StartOffsetSeconds.Should().Be(1); // con_start = 0 + stagger[3](0) + 1
        plan.Items[1].StartOffsetSeconds.Should().Be(2);
        plan.Items[2].StartOffsetSeconds.Should().Be(3);
    }

    // --- 26: Mixed sequential + parallel in one pass ---
    [Fact]
    public void Mixed_sequential_and_parallel_zones_schedule_independently()
    {
        var plan = StationScheduler.Plan(Request(new[]
        {
            Pending(1, ZoneGroup.Sequential0, 60),
            Pending(2, ZoneGroup.Parallel, 60),
        }));

        plan.Items[0].StartOffsetSeconds.Should().Be(1); // sequential group 0 at stagger[0]
        plan.Items[1].StartOffsetSeconds.Should().Be(2); // concurrent base = stagger[3](1) + 1
    }

    // --- 27: last_seq_stop_times carryover in append mode, main.cpp:1559-1561 ---
    [Fact]
    public void Append_mode_starts_after_the_groups_current_last_stop()
    {
        var existing = new[] { new QueueItem(9, 9, ZoneGroup.Sequential0, StartOffsetSeconds: 0, DurationSeconds: 100, DequeueOffsetSeconds: 100, ProgramId: 1) };
        var plan = StationScheduler.Plan(Request(
            new[] { Pending(1, ZoneGroup.Sequential0, 60) }, delay: 10, existing: existing));

        plan.Items.Should().HaveCount(2);
        plan.Items[0].StartOffsetSeconds.Should().Be(0);   // existing unchanged
        plan.Items[1].StartOffsetSeconds.Should().Be(110); // 100 last-stop + 10 delay
    }

    // --- 28: Insert-front shifts a waiting existing zone, main.cpp:1539-1541,1550-1552 ---
    [Fact]
    public void Insert_front_shifts_waiting_existing_zones_and_preempts()
    {
        var existing = new[] { new QueueItem(9, 9, ZoneGroup.Sequential0, StartOffsetSeconds: 50, DurationSeconds: 100, DequeueOffsetSeconds: 150, ProgramId: 1) };
        var plan = StationScheduler.Plan(Request(
            new[] { Pending(1, ZoneGroup.Sequential0, 30) }, delay: 10, insertFront: true, existing: existing));

        // adjustment = seqAdj(30+10) + stagger(1) = 41
        plan.Items[0].StartOffsetSeconds.Should().Be(91);  // existing 50 + 41
        plan.Items[0].DequeueOffsetSeconds.Should().Be(191); // 150 + 41
        plan.Items[1].StartOffsetSeconds.Should().Be(1);   // new zone preempts at the front
        plan.Items[1].DequeueOffsetSeconds.Should().Be(31);
    }

    // --- 29: Insert-front trims a currently-running existing zone, main.cpp:1531-1537 ---
    [Fact]
    public void Insert_front_trims_a_running_existing_zone()
    {
        // Running 30s ago, 70s remaining.
        var existing = new[] { new QueueItem(9, 9, ZoneGroup.Sequential0, StartOffsetSeconds: -30, DurationSeconds: 100, DequeueOffsetSeconds: 70, ProgramId: 1) };
        var plan = StationScheduler.Plan(Request(
            new[] { Pending(1, ZoneGroup.Sequential0, 20) }, delay: 5, insertFront: true, existing: existing));

        // adjustment = seqAdj(20+5) + stagger(1) = 26
        plan.Items[0].StartOffsetSeconds.Should().Be(26);  // rescheduled after the new zone
        plan.Items[0].DurationSeconds.Should().Be(70);     // trimmed to remaining
        plan.Items[0].DequeueOffsetSeconds.Should().Be(96); // 70 + 26
        plan.Items[1].StartOffsetSeconds.Should().Be(1);   // new zone at the front
    }

    // --- 30: Master on/off adjustment, with start pushed when the lead falls in the past, main.cpp:1454-1466 ---
    [Fact]
    public void Master_adjustment_sets_dequeue_and_pushes_start_for_a_negative_lead()
    {
        var master = MasterBinding.Create(masterHardwareBit: 15, onAdjustConfigured: -15, offAdjustConfigured: 20, boundZoneIds: new[] { 1 });
        var plan = StationScheduler.Plan(Request(
            new[] { Pending(1, ZoneGroup.Sequential0, 60) }, masters: new[] { master }));

        var item = plan.Items[0];
        item.StartOffsetSeconds.Should().Be(16);   // 1 + |on adj 15| (start was within the lead window)
        item.DequeueOffsetSeconds.Should().Be(96);  // 16 + 60 + 20 off adj
    }

    // --- 31: Master 0-adjustments coerce to -1 / +1, OpenSprinkler.cpp:1745,1750 ---
    [Fact]
    public void Master_zero_adjustments_coerce_to_one_second_stagger()
    {
        var master = MasterBinding.Create(masterHardwareBit: 15, onAdjustConfigured: 0, offAdjustConfigured: 0, boundZoneIds: new[] { 1 });
        master.OnAdjustSeconds.Should().Be(-1);
        master.OffAdjustSeconds.Should().Be(1);

        var plan = StationScheduler.Plan(Request(
            new[] { Pending(1, ZoneGroup.Sequential0, 60) }, masters: new[] { master }));

        plan.Items[0].StartOffsetSeconds.Should().Be(2);   // 1 + |on adj 1|
        plan.Items[0].DequeueOffsetSeconds.Should().Be(63); // 2 + 60 + 1
    }

    // --- 32: MasterShouldBeOn window edges, main.cpp:1081-1082 ---
    [Fact]
    public void Master_should_be_on_within_the_window_only()
    {
        var master = MasterBinding.Create(15, onAdjustConfigured: -5, offAdjustConfigured: 5, boundZoneIds: new[] { 1 });
        var items = ImmutableArray.Create(new QueueItem(1, 1, ZoneGroup.Sequential0, StartOffsetSeconds: 10, DurationSeconds: 30, DequeueOffsetSeconds: 45, ProgramId: 1));
        // window = [10 - 5, 10 + 30 + 5] = [5, 45]
        StationScheduler.MasterShouldBeOn(master, items, 4).Should().BeFalse();
        StationScheduler.MasterShouldBeOn(master, items, 5).Should().BeTrue();
        StationScheduler.MasterShouldBeOn(master, items, 45).Should().BeTrue();
        StationScheduler.MasterShouldBeOn(master, items, 46).Should().BeFalse();
    }

    // --- 33: Master bound to two zones stays on across overlap, off in a real gap ---
    [Fact]
    public void Master_spans_two_bound_zones_and_drops_in_a_gap()
    {
        var master = MasterBinding.Create(15, onAdjustConfigured: 0, offAdjustConfigured: 0, boundZoneIds: new[] { 1, 2 });

        // Back-to-back: A [-1,11], B [10,22] overlap at 10..11 -> continuously on.
        var contiguous = ImmutableArray.Create(
            new QueueItem(1, 1, ZoneGroup.Sequential0, 0, 10, 11, 1),
            new QueueItem(2, 2, ZoneGroup.Sequential0, 11, 10, 22, 1));
        StationScheduler.MasterShouldBeOn(master, contiguous, 11).Should().BeTrue();

        // Real gap: A [-1,11], B [19,31]; at t=15 neither covers -> off.
        var gapped = ImmutableArray.Create(
            new QueueItem(1, 1, ZoneGroup.Sequential0, 0, 10, 11, 1),
            new QueueItem(2, 2, ZoneGroup.Sequential0, 20, 10, 31, 1));
        StationScheduler.MasterShouldBeOn(master, gapped, 15).Should().BeFalse();
    }
}
