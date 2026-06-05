using System.Diagnostics;
using FluentAssertions;
using OSPi.Application.Engine;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;

namespace OSPi.Tests.Engine;

/// <summary>
/// Chunk-A integration tests: the engine wires the pure scheduler into the per-minute /
/// per-second runtime. Driven synchronously via <see cref="EngineHarness"/> so a simulated
/// timeline is deterministic. Anchored at 2024-06-03 06:00:00Z (UTC offset 0), so tick k runs
/// at that instant + k seconds and the first tick lands in civil minute 360 (06:00).
/// </summary>
public class EngineSchedulingTests
{
    private static readonly DateTimeOffset Anchor = new(2024, 6, 3, 6, 0, 0, TimeSpan.Zero);
    private const int StartMinute = 360; // 06:00

    private static int OnCount(bool[] frame) => frame.Count(on => on);

    [Fact]
    public void Sequential_program_runs_zones_in_run_order_with_station_delay()
    {
        var data = Build.Data(
            Build.Settings(stationDelay: 2),
            Build.Zones16(),
            new[] { Build.FixedDaily(1, StartMinute, (1, 4, 0), (2, 4, 1), (3, 4, 2)) });
        var h = new EngineHarness(data, Anchor);

        h.Steps(18);

        // A@[k2,k5], gap k6-7 (station delay), B@[k8,k11], C@[k14,k17].
        h.FrameAt(3).Should().BeEquivalentTo(OnlyBit(0));
        h.FrameAt(7).Should().OnlyContain(on => on == false); // station-delay gap, nothing on
        h.FrameAt(9).Should().BeEquivalentTo(OnlyBit(1));
        h.FrameAt(15).Should().BeEquivalentTo(OnlyBit(2));

        // SecondsRemaining counts down; the zone reports its owning program.
        h.SnapshotAt(2).Zones[0].SecondsRemaining.Should().Be(4);
        h.SnapshotAt(2).Zones[0].ProgramId.Should().Be(1);
        h.SnapshotAt(5).Zones[0].SecondsRemaining.Should().Be(1);
    }

    [Fact]
    public void Dst_aware_timezone_fires_a_fixed_start_an_hour_earlier_in_utc_in_summer_than_winter()
    {
        // A program fixed at 18:40 civil, in America/Denver (DST: UTC-6 summer, UTC-7 winter).
        // Summer civil 18:40 == 00:40Z; winter civil 18:40 == 01:40Z. Proves matching tracks DST
        // rather than a fixed offset, which is the bug behind "program didn't start at 6:40pm".
        const int SixFortyPm = 18 * 60 + 40; // 1120

        EngineHarness Harness(DateTimeOffset startUtc)
        {
            var settings = Build.Settings();
            settings.TimeZoneId = "America/Denver";
            var data = Build.Data(settings, Build.Zones16(),
                new[] { Build.FixedDaily(1, SixFortyPm, (1, 60, 0)) });
            return new EngineHarness(data, startUtc);
        }

        // Summer: fires as civil time crosses 18:40 at 00:40Z (tick 30 from 00:39:30Z).
        var summer = Harness(new DateTimeOffset(2026, 7, 2, 0, 39, 30, TimeSpan.Zero));
        summer.Steps(33);
        summer.FrameAt(31).Should().BeEquivalentTo(OnlyBit(0));

        // Winter at the SAME 00:40Z is only 17:40 civil — must not fire.
        var winterEarly = Harness(new DateTimeOffset(2026, 1, 2, 0, 39, 30, TimeSpan.Zero));
        winterEarly.Steps(33);
        Enumerable.Range(1, 33).Should().OnlyContain(k => OnCount(winterEarly.FrameAt(k)) == 0);

        // Winter fires an hour later, at 01:40Z.
        var winterOnTime = Harness(new DateTimeOffset(2026, 1, 2, 1, 39, 30, TimeSpan.Zero));
        winterOnTime.Steps(33);
        winterOnTime.FrameAt(31).Should().BeEquivalentTo(OnlyBit(0));
    }

    [Fact]
    public void Parallel_group_runs_zones_concurrently()
    {
        var zones = Build.Zones16();
        foreach (var z in zones.Where(z => z.Id is 1 or 2 or 3)) z.Group = ZoneGroup.Parallel;

        var data = Build.Data(
            Build.Settings(),
            zones,
            new[] { Build.FixedDaily(1, StartMinute, (1, 4, 0), (2, 4, 1), (3, 4, 2)) });
        var h = new EngineHarness(data, Anchor);

        h.Steps(6);

        // All three start within 1s of each other and overlap by tick 5.
        var frame = h.FrameAt(5);
        OnCount(frame).Should().Be(3);
        frame[0].Should().BeTrue();
        frame[1].Should().BeTrue();
        frame[2].Should().BeTrue();
    }

    [Fact]
    public void Master_station_turns_on_with_lead_and_off_with_lag()
    {
        var zones = Build.Zones16();
        zones.Single(z => z.Id == 1).BoundToMaster1 = true; // bit 0
        var masters = new[]
        {
            new MasterStation { Id = 1, MasterIndex = 1, ZoneId = 16, OnAdjustSeconds = -2, OffAdjustSeconds = 3 },
        };

        var data = Build.Data(
            Build.Settings(),
            zones,
            new[] { Build.FixedDaily(1, StartMinute, (1, 4, 0)) },
            masters);
        var h = new EngineHarness(data, Anchor);

        h.Steps(12);

        // The negative master lead pushes the zone start back (firmware handle_master_adjustments):
        // zone A runs E+4..E+8 (bit 0, ticks 4..7); the master (bit 15) leads 2s and lags 3s → ticks 2..11.
        h.FrameAt(2)[15].Should().BeTrue("master leads the zone start");
        h.FrameAt(2)[0].Should().BeFalse();
        h.FrameAt(5)[0].Should().BeTrue();
        h.FrameAt(5)[15].Should().BeTrue();
        h.FrameAt(9)[0].Should().BeFalse("zone A already finished");
        h.FrameAt(9)[15].Should().BeTrue("master lags after the zone stops");
        h.FrameAt(12)[15].Should().BeFalse();
    }

    [Fact]
    public void Rain_delay_suppresses_non_ignoring_zones_but_runs_ignoring_zones()
    {
        var zones = Build.Zones16();
        zones.Single(z => z.Id == 4).IgnoreRain = true; // bit 3 ignores rain

        var data = Build.Data(
            Build.Settings(),
            zones,
            new[] { Build.FixedDaily(1, StartMinute, (1, 4, 0), (4, 4, 1)) });
        var h = new EngineHarness(data, Anchor);

        h.Engine.Post(new EngineCommand.SetRainDelay(60));
        h.Steps(8);

        // Zone 1 (bit 0) never runs; zone 4 (bit 3) runs because it ignores rain.
        Enumerable.Range(1, 8).Select(h.FrameAt).Should().OnlyContain(f => f[0] == false);
        h.FrameAt(3)[3].Should().BeTrue();
        h.Snapshot.RainDelayUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task Single_run_program_is_deleted_after_it_matches()
    {
        var program = new Domain.Entities.Program
        {
            Id = 1,
            Name = "Once",
            Enabled = true,
            UseWeather = false,
            ScheduleType = ScheduleType.SingleRun,
            SingleRunDate = new DateOnly(2024, 6, 3),
            StartTimeType = StartTimeType.Fixed,
            StartTimes = { new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = StartMinute } },
        };
        program.ZoneDurations.Add(new ProgramZoneDuration { ProgramId = 1, ZoneId = 1, DurationSeconds = 4, RunOrder = 0 });

        var data = Build.Data(Build.Settings(), Build.Zones16(), new[] { program });
        var h = new EngineHarness(data, Anchor);

        h.Steps(4);

        h.FrameAt(3)[0].Should().BeTrue("the single run still executes the minute it matches");
        await WaitUntilAsync(() => { lock (h.Programs.DeletedIds) return h.Programs.DeletedIds.Contains(1); });
    }

    [Fact]
    public void Water_level_below_threshold_skips_short_zones_but_scales_long_ones()
    {
        var program = Build.FixedDaily(1, StartMinute, (1, 60, 0), (2, 120, 1));
        program.UseWeather = true; // subject to water-level scaling

        var data = Build.Data(
            Build.Settings(waterLevel: 15),
            Build.Zones16(),
            new[] { program });
        var h = new EngineHarness(data, Anchor);

        h.Steps(5);

        // Zone 1: 60s * 15% = 9s, and (wl<20 && <10s) ⇒ skipped entirely.
        Enumerable.Range(1, 5).Select(h.FrameAt).Should().OnlyContain(f => f[0] == false);
        // Zone 2: 120s * 15% = 18s ⇒ runs.
        h.FrameAt(3)[1].Should().BeTrue();
    }

    [Fact]
    public void Water_level_is_ignored_when_program_does_not_use_weather()
    {
        var program = Build.FixedDaily(1, StartMinute, (1, 60, 0));
        program.UseWeather = false; // ignores WaterLevelPercent

        var data = Build.Data(
            Build.Settings(waterLevel: 15),
            Build.Zones16(),
            new[] { program });
        var h = new EngineHarness(data, Anchor);

        h.Steps(4);

        h.FrameAt(3)[0].Should().BeTrue("a non-weather program is not scaled and runs the full duration");
    }

    [Fact]
    public void Manual_run_program_queues_behind_a_running_sequential_zone()
    {
        var scheduled = Build.FixedDaily(1, StartMinute, (1, 10, 0));      // zone 1 (bit 0), 10s
        var manual = Build.FixedDaily(2, 720, (2, 4, 0));                  // zone 2 (bit 1); never auto-matches at 06:00

        var data = Build.Data(
            Build.Settings(stationDelay: 0),
            Build.Zones16(),
            new[] { scheduled, manual });
        var h = new EngineHarness(data, Anchor);

        h.Steps(4);
        h.FrameAt(4)[0].Should().BeTrue("the scheduled zone is running before the manual start");

        h.Engine.Post(new EngineCommand.RunProgram(2));
        h.Steps(11); // ticks 5..15

        // The running zone is not preempted: it keeps running its full 10s (frames 2..11).
        h.FrameAt(7)[0].Should().BeTrue("the running zone keeps going; the manual run does not preempt it");
        h.FrameAt(7)[1].Should().BeFalse("the manual zone is queued behind, not started immediately");
        h.SnapshotAt(5).Zones[1].Queued.Should().BeTrue("the manual zone is queued behind the running zone");
        h.FrameAt(11)[0].Should().BeTrue("the running zone finishes its full duration");

        // The manual zone runs only after the scheduled zone completes (frames 12..15).
        h.FrameAt(12)[0].Should().BeFalse();
        h.FrameAt(12)[1].Should().BeTrue("the manual zone starts after the running zone finishes");
        h.FrameAt(15)[1].Should().BeTrue();
    }

    [Fact]
    public void Cancelling_a_running_sequential_zone_pulls_the_queued_zone_forward()
    {
        var first = Build.FixedDaily(1, StartMinute, (1, 10, 0)); // zone 1 (bit 0), 10s, auto-matches at 06:00
        var second = Build.FixedDaily(2, 720, (2, 4, 0));         // zone 2 (bit 1), manual (won't match at 06:00)
        var data = Build.Data(Build.Settings(stationDelay: 0), Build.Zones16(), new[] { first, second });
        var h = new EngineHarness(data, Anchor);

        h.Steps(4);
        h.FrameAt(4)[0].Should().BeTrue("zone 1 is running");

        h.Engine.Post(new EngineCommand.RunProgram(2));
        h.Step(); // k5: zone 2 queued behind zone 1
        h.SnapshotAt(5).Zones[1].Queued.Should().BeTrue();

        h.Engine.Post(new EngineCommand.CancelZone(0));
        h.Steps(5); // k6: zone 1 cancelled, zone 2 pulls forward (delay 0 ⇒ immediate); run k7..k10

        h.FrameAt(6)[0].Should().BeFalse("zone 1 stopped");
        h.FrameAt(6)[1].Should().BeTrue("zone 2 starts now instead of waiting for zone 1's original full 10s");
        h.SnapshotAt(6).Zones[1].Queued.Should().BeFalse("zone 2 is running, not queued");
        h.FrameAt(9)[1].Should().BeTrue("zone 2 runs its full 4s (k6..k9)");
        h.FrameAt(10)[1].Should().BeFalse();
    }

    [Fact]
    public void Cancelling_a_running_sequential_zone_honors_station_delay_before_the_next_zone()
    {
        var first = Build.FixedDaily(1, StartMinute, (1, 10, 0)); // zone 1 (bit 0), 10s
        var second = Build.FixedDaily(2, 720, (2, 4, 0));         // zone 2 (bit 1), manual
        var data = Build.Data(Build.Settings(stationDelay: 2), Build.Zones16(), new[] { first, second });
        var h = new EngineHarness(data, Anchor);

        h.Steps(4);
        h.FrameAt(4)[0].Should().BeTrue("zone 1 is running");

        h.Engine.Post(new EngineCommand.RunProgram(2));
        h.Step(); // k5: zone 2 queued behind zone 1
        h.SnapshotAt(5).Zones[1].Queued.Should().BeTrue();

        h.Engine.Post(new EngineCommand.CancelZone(0));
        h.Steps(7); // k6: zone 1 cancelled; zone 2 starts after the 2s station delay (k8); run k7..k12

        h.FrameAt(6)[0].Should().BeFalse("zone 1 stopped");
        h.FrameAt(6)[1].Should().BeFalse("station delay: zone 2 waits 2s after the early stop");
        h.FrameAt(7)[1].Should().BeFalse("still within the station-delay gap");
        h.SnapshotAt(7).Zones[1].Queued.Should().BeTrue("zone 2 is queued during the delay, not dropped");
        h.FrameAt(8)[1].Should().BeTrue("zone 2 starts after the 2s station delay");
        h.FrameAt(11)[1].Should().BeTrue("zone 2 runs its full 4s (k8..k11)");
        h.FrameAt(12)[1].Should().BeFalse();
    }

    [Fact]
    public void Pause_freezes_the_running_zone_and_resumes_it_with_full_remaining_time()
    {
        // Zone 1 runs k2..k11 (10s) when undisturbed; it would turn off at k12.
        var data = Build.Data(
            Build.Settings(),
            Build.Zones16(),
            new[] { Build.FixedDaily(1, StartMinute, (1, 10, 0)) });
        var h = new EngineHarness(data, Anchor);

        h.Steps(3);
        h.FrameAt(3)[0].Should().BeTrue();

        h.Engine.Post(new EngineCommand.Pause(3));
        h.Step(); // k4: pause begins (2s of run elapsed, 8s remaining)
        h.FrameAt(4)[0].Should().BeFalse("output is suppressed while paused");

        var s4 = h.SnapshotAt(4);
        s4.Paused.Should().BeTrue();
        s4.PauseSecondsRemaining.Should().Be(3);
        s4.Zones[0].On.Should().BeFalse("the valve is off while paused");
        s4.Zones[0].Paused.Should().BeTrue("the zone is frozen mid-run, not merely off");
        s4.Zones[0].SecondsRemaining.Should().Be(8, "the remaining run time is frozen at the pause");

        // The frozen countdown does not tick while the pause counts down.
        h.Step(); // k5
        h.SnapshotAt(5).PauseSecondsRemaining.Should().Be(2);
        h.SnapshotAt(5).Zones[0].SecondsRemaining.Should().Be(8);
        h.Step(); // k6: last paused second
        h.SnapshotAt(6).Zones[0].SecondsRemaining.Should().Be(8);

        h.Step(); // k7: pause expired, zone resumes
        h.SnapshotAt(7).Paused.Should().BeFalse();
        h.FrameAt(7)[0].Should().BeTrue("the zone resumes when the pause expires");
        h.SnapshotAt(7).Zones[0].On.Should().BeTrue();
        h.SnapshotAt(7).Zones[0].SecondsRemaining.Should().Be(8, "it resumes with its full remaining time");

        // It runs the full remaining 8s (k7..k14) and turns off at k15 — pushed back by the 3s pause.
        h.Steps(8); // k8..k15
        h.FrameAt(14)[0].Should().BeTrue();
        h.FrameAt(15)[0].Should().BeFalse();
    }

    [Fact]
    public void Resume_before_the_pause_expires_restarts_the_frozen_zone_immediately()
    {
        var data = Build.Data(
            Build.Settings(),
            Build.Zones16(),
            new[] { Build.FixedDaily(1, StartMinute, (1, 10, 0)) });
        var h = new EngineHarness(data, Anchor);

        h.Steps(3);
        h.Engine.Post(new EngineCommand.Pause(3));
        h.Step(); // k4: paused
        h.FrameAt(4)[0].Should().BeFalse();

        h.Engine.Post(new EngineCommand.Resume());
        h.Step(); // k5: resume processed; queue pulled back with a 1s scheduler nudge
        h.SnapshotAt(5).Paused.Should().BeFalse();

        h.Steps(9); // k6..k14
        h.FrameAt(6)[0].Should().BeTrue("the frozen zone restarts right after an early resume");
        // Full remaining 8s runs k6..k13, off at k14.
        h.FrameAt(13)[0].Should().BeTrue();
        h.FrameAt(14)[0].Should().BeFalse();
    }

    private static bool[] OnlyBit(int bit)
    {
        var arr = new bool[16];
        arr[bit] = true;
        return arr;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(5);
        }
    }
}
