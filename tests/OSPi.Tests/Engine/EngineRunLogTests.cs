using FluentAssertions;
using OSPi.Application.Engine;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;

namespace OSPi.Tests.Engine;

/// <summary>
/// Chunk-A run-log tests: the engine emits a <see cref="RunLogEntry"/> for each contiguous zone
/// run as it ends. Driven synchronously via <see cref="EngineHarness"/> with a
/// <see cref="RecordingRunLogWriter"/>, so writes are observed deterministically on the tick
/// thread. Anchored at 2024-06-03 06:00:00Z (UTC offset 0): tick k runs at Anchor + k seconds, so
/// a fixed-start zone in a single sequential group runs ticks 2..(2+dur-1) and logs at tick 2+dur.
/// Zone ids are 1-based; hardware bit b maps to zone id b+1 (see <see cref="Build.Zones16"/>).
/// </summary>
public class EngineRunLogTests
{
    private static readonly DateTimeOffset Anchor = new(2024, 6, 3, 6, 0, 0, TimeSpan.Zero);
    private const int StartMinute = 360; // 06:00

    [Fact]
    public void A_completed_program_run_logs_one_entry_with_real_zone_id_program_and_duration()
    {
        var data = Build.Data(
            Build.Settings(),
            Build.Zones16(),
            new[] { Build.FixedDaily(1, StartMinute, (1, 4, 0)) });
        var h = new EngineHarness(data, Anchor);

        h.Steps(8);

        h.RunLog.Entries.Should().ContainSingle();
        var e = h.RunLog.Entries[0];
        e.ZoneId.Should().Be(1, "the queue item carries the real Zone.Id, not the hardware bit");
        e.ProgramId.Should().Be(1);
        e.DurationSeconds.Should().Be(4);
        e.EndTime.Should().Be(e.StartTime.AddSeconds(4));
    }

    [Fact]
    public void Sequential_program_logs_one_entry_per_zone_in_run_order()
    {
        var data = Build.Data(
            Build.Settings(stationDelay: 0),
            Build.Zones16(),
            new[] { Build.FixedDaily(1, StartMinute, (1, 4, 0), (2, 4, 1), (3, 4, 2)) });
        var h = new EngineHarness(data, Anchor);

        h.Steps(16);

        h.RunLog.Entries.Select(e => e.ZoneId).Should().Equal(1, 2, 3);
        h.RunLog.Entries.Should().OnlyContain(e => e.ProgramId == 1 && e.DurationSeconds == 4);
    }

    [Fact]
    public void A_pause_mid_run_logs_a_single_run_spanning_the_pause()
    {
        var data = Build.Data(
            Build.Settings(),
            Build.Zones16(),
            new[] { Build.FixedDaily(1, StartMinute, (1, 10, 0)) });
        var h = new EngineHarness(data, Anchor);

        h.Steps(3);
        h.Engine.Post(new EngineCommand.Pause(3));
        h.Steps(2);                          // ticks 4-5: output suppressed, queue keeps advancing
        h.RunLog.Entries.Should().BeEmpty("the run is still open while paused");

        h.Steps(7);                          // ticks 6-12: pause expires, run completes at tick 12

        h.RunLog.Entries.Should().ContainSingle("a pause must not split one run into two rows");
        h.RunLog.Entries[0].DurationSeconds.Should().Be(10);
        h.RunLog.Entries[0].ProgramId.Should().Be(1);
    }

    [Fact]
    public void Preemption_splits_the_preempted_run_into_two_history_rows()
    {
        var scheduled = Build.FixedDaily(1, StartMinute, (1, 10, 0)); // zone 1 (bit 0), 10s
        var manual = Build.FixedDaily(2, 720, (2, 4, 0));             // zone 2 (bit 1); never auto-matches at 06:00

        var data = Build.Data(
            Build.Settings(stationDelay: 0),
            Build.Zones16(),
            new[] { scheduled, manual });
        var h = new EngineHarness(data, Anchor);

        h.Steps(4);
        h.Engine.Post(new EngineCommand.RunProgram(2));
        h.Steps(13); // run through the manual preempt and the scheduled zone's resumed remainder

        var manualRuns = h.RunLog.Entries.Where(e => e.ZoneId == 2).ToList();
        var scheduledRuns = h.RunLog.Entries.Where(e => e.ZoneId == 1).ToList();

        manualRuns.Should().ContainSingle();
        manualRuns[0].ProgramId.Should().Be(2);
        manualRuns[0].DurationSeconds.Should().Be(4);

        scheduledRuns.Should().HaveCount(2, "preemption ends one run early and the remainder logs separately");
        scheduledRuns.Should().OnlyContain(e => e.ProgramId == 1);
        scheduledRuns.Sum(e => e.DurationSeconds).Should().Be(10, "the total scheduled time is preserved across the split");
    }

    [Fact]
    public void Indefinite_manual_zone_toggles_are_not_logged()
    {
        var data = Build.Data(Build.Settings(), Build.Zones16());
        var h = new EngineHarness(data, Anchor);

        h.Engine.Post(new EngineCommand.SetZone(0, true));
        h.Steps(5);
        h.Engine.Post(new EngineCommand.SetZone(0, false));
        h.Steps(2);

        h.RunLog.Entries.Should().BeEmpty("indefinite manual toggles have no duration/program and are excluded");
    }

    [Fact]
    public void A_timed_manual_zone_run_logs_with_a_null_program()
    {
        var data = Build.Data(Build.Settings(), Build.Zones16());
        var h = new EngineHarness(data, Anchor);

        h.Engine.Post(new EngineCommand.RunZoneTimed(0, 4)); // hardware bit 0 → zone id 1
        h.Steps(7);

        h.RunLog.Entries.Should().ContainSingle();
        h.RunLog.Entries[0].ZoneId.Should().Be(1);
        h.RunLog.Entries[0].ProgramId.Should().BeNull();
        h.RunLog.Entries[0].DurationSeconds.Should().Be(4);
    }

    [Fact]
    public void Stop_all_mid_run_logs_the_observed_short_duration()
    {
        var data = Build.Data(
            Build.Settings(),
            Build.Zones16(),
            new[] { Build.FixedDaily(1, StartMinute, (1, 10, 0)) });
        var h = new EngineHarness(data, Anchor);

        h.Steps(4); // zone 1 on ticks 2..4 (would run to tick 11)
        h.Engine.Post(new EngineCommand.StopAll());
        h.Step();   // tick 5: queue cleared, run closes with its observed duration

        h.RunLog.Entries.Should().ContainSingle();
        h.RunLog.Entries[0].ZoneId.Should().Be(1);
        h.RunLog.Entries[0].DurationSeconds.Should().Be(3, "the run is cut short, not logged as its planned 10s");
    }

    [Fact]
    public void Cancel_zone_stops_only_that_zone_and_logs_its_observed_duration()
    {
        // Two parallel zones so we can prove CancelZone stops one without touching the other.
        var zones = Build.Zones16();
        zones[0].Group = ZoneGroup.Parallel;
        zones[1].Group = ZoneGroup.Parallel;
        var data = Build.Data(
            Build.Settings(),
            zones,
            new[] { Build.FixedDaily(1, StartMinute, (1, 10, 0), (2, 10, 1)) });
        var h = new EngineHarness(data, Anchor);

        h.Steps(4);                  // bits 0 and 1 both on from tick 2 (parallel)
        h.FrameAt(4)[0].Should().BeTrue();
        h.FrameAt(4)[1].Should().BeTrue();

        h.Engine.Post(new EngineCommand.CancelZone(0)); // stop zone 1 (hardware bit 0)
        h.Step();                    // tick 5: bit 0's queue item dropped, its run closes

        var frame = h.FrameAt(5);
        frame[0].Should().BeFalse("the cancelled zone stops immediately");
        frame[1].Should().BeTrue("the other zone keeps running");

        h.RunLog.Entries.Should().ContainSingle("only the cancelled run has closed");
        h.RunLog.Entries[0].ZoneId.Should().Be(1);
        h.RunLog.Entries[0].DurationSeconds.Should().Be(3, "cut short, not its planned 10s");
    }
}
