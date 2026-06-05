using System.Collections.Immutable;
using FluentAssertions;
using OSPi.Application.Engine;
using OSPi.Application.Persistence;
using OSPi.Application.Services;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;
using OSPi.Mcp;

namespace OSPi.Mcp.Tests;

public class SprinklerToolsTests
{
    // ---- run_zone: zone number -> hardware bit, minutes -> seconds ----

    [Fact]
    public void RunZone_translates_zone_number_to_hardware_bit_and_minutes_to_seconds()
    {
        var manual = new FakeManualRunService();

        var result = SprinklerTools.RunZone(zoneNumber: 3, minutes: 5, manual);

        manual.TimedRuns.Should().ContainSingle()
            .Which.Should().Be((2, 300)); // zone 3 -> bit 2, 5 min -> 300 s
        result.Ok.Should().BeTrue();
        result.Message.Should().Contain("Zone 3");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(-1)]
    public void RunZone_rejects_out_of_range_zone_numbers(int zoneNumber)
    {
        var manual = new FakeManualRunService();

        var act = () => SprinklerTools.RunZone(zoneNumber, minutes: 5, manual);

        act.Should().Throw<ArgumentOutOfRangeException>();
        manual.TimedRuns.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void RunZone_rejects_non_positive_minutes(int minutes)
    {
        var manual = new FakeManualRunService();

        var act = () => SprinklerTools.RunZone(zoneNumber: 1, minutes, manual);

        act.Should().Throw<ArgumentOutOfRangeException>();
        manual.TimedRuns.Should().BeEmpty();
    }

    // ---- stop_zone: zone number -> hardware bit (per-zone cancel) ----

    [Fact]
    public void StopZone_translates_zone_number_to_hardware_bit()
    {
        var manual = new FakeManualRunService();

        var result = SprinklerTools.StopZone(zoneNumber: 3, manual);

        manual.StoppedZones.Should().ContainSingle().Which.Should().Be(2); // zone 3 -> bit 2
        result.Message.Should().Contain("Zone 3");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void StopZone_rejects_out_of_range_zone_numbers(int zoneNumber)
    {
        var manual = new FakeManualRunService();

        var act = () => SprinklerTools.StopZone(zoneNumber, manual);

        act.Should().Throw<ArgumentOutOfRangeException>();
        manual.StoppedZones.Should().BeEmpty();
    }

    // ---- get_status: null snapshot and projection ----

    [Fact]
    public void GetStatus_reports_engine_not_ready_when_no_snapshot()
    {
        var hub = new FakeStateHub(null);

        var status = SprinklerTools.GetStatus(hub);

        status.EngineReady.Should().BeFalse();
        status.Zones.Should().BeEmpty();
    }

    [Fact]
    public void GetStatus_projects_snapshot_and_offsets_zone_numbers()
    {
        var snapshot = new StatusSnapshot
        {
            TimestampUtc = DateTimeOffset.UnixEpoch,
            SystemEnabled = true,
            Paused = true,
            RainDelayUntil = DateTimeOffset.UnixEpoch.AddHours(2),
            WaterLevelPercent = 80,
            Zones =
            [
                new ZoneStatus { ZoneId = 0, On = true, SecondsRemaining = 120, ProgramId = 7, Queued = false },
                new ZoneStatus { ZoneId = 1, On = false, Queued = true },
            ],
        };
        var hub = new FakeStateHub(snapshot);

        var status = SprinklerTools.GetStatus(hub);

        status.EngineReady.Should().BeTrue();
        status.Paused.Should().BeTrue();
        status.WaterLevelPercent.Should().Be(80);
        status.RainDelayUntil.Should().Be(DateTimeOffset.UnixEpoch.AddHours(2));
        status.Zones.Should().HaveCount(2);

        var first = status.Zones[0];
        first.Number.Should().Be(1); // ZoneId 0 -> number 1
        first.On.Should().BeTrue();
        first.SecondsRemaining.Should().Be(120);
        first.ProgramId.Should().Be(7);

        status.Zones[1].Number.Should().Be(2);
        status.Zones[1].Queued.Should().BeTrue();
    }

    // ---- list_zones / list_programs projection ----

    [Fact]
    public async Task ListZones_projects_and_orders_by_hardware_bit_with_name_fallback()
    {
        var zones = new FakeZoneRepository(
            new Zone { Id = 2, HardwareBit = 1, Name = "  ", Group = ZoneGroup.Parallel, BoundToMaster1 = true },
            new Zone { Id = 1, HardwareBit = 0, Name = "Front Lawn", Group = ZoneGroup.Sequential0, Disabled = true });

        var result = await SprinklerTools.ListZonesAsync(zones);

        result.Should().HaveCount(2);
        result[0].Number.Should().Be(1); // hardware bit 0 sorts first
        result[0].Name.Should().Be("Front Lawn");
        result[0].Disabled.Should().BeTrue();

        result[1].Number.Should().Be(2);
        result[1].Name.Should().Be("Zone 2"); // blank name -> fallback
        result[1].Group.Should().Be("Parallel");
        result[1].BoundToMaster1.Should().BeTrue();
    }

    [Fact]
    public async Task ListPrograms_projects_id_name_enabled_schedule()
    {
        var programs = new FakeProgramRepository(
            new Program { Id = 5, Name = "Morning", Enabled = false, ScheduleType = ScheduleType.Interval });

        var result = await SprinklerTools.ListProgramsAsync(programs);

        result.Should().ContainSingle();
        result[0].Should().Be(new ProgramInfo(5, "Morning", false, "Interval"));
    }

    // ---- run_program / set_rain_delay ----

    [Fact]
    public async Task RunProgram_resolves_name_and_drives_service()
    {
        var programs = new FakeProgramRepository(new Program { Id = 9, Name = "Evening" });
        var manual = new FakeManualRunService();

        var result = await SprinklerTools.RunProgramAsync(programId: 9, manual, programs);

        manual.RanPrograms.Should().ContainSingle().Which.Should().Be(9);
        result.Message.Should().Contain("Evening");
    }

    [Fact]
    public async Task RunProgram_throws_when_program_missing()
    {
        var programs = new FakeProgramRepository();
        var manual = new FakeManualRunService();

        var act = async () => await SprinklerTools.RunProgramAsync(programId: 9, manual, programs);

        await act.Should().ThrowAsync<ArgumentException>();
        manual.RanPrograms.Should().BeEmpty();
    }

    [Fact]
    public void SetRainDelay_messages_differ_for_set_and_clear()
    {
        var manual = new FakeManualRunService();

        SprinklerTools.SetRainDelay(60, manual).Message.Should().Contain("60");
        SprinklerTools.SetRainDelay(0, manual).Message.Should().Contain("cleared");

        manual.RainDelays.Should().Equal(60, 0);
    }

    // ---- fakes ----

    private sealed class FakeManualRunService : IManualRunService
    {
        public List<(int bit, int seconds)> TimedRuns { get; } = new();
        public List<int> StoppedZones { get; } = new();
        public List<int> RanPrograms { get; } = new();
        public List<int> RainDelays { get; } = new();

        public void RunZoneTimed(int hardwareBit, int seconds) => TimedRuns.Add((hardwareBit, seconds));
        public void StopZone(int hardwareBit) => StoppedZones.Add(hardwareBit);
        public void RunProgram(int programId) => RanPrograms.Add(programId);
        public void SetRainDelay(int minutes) => RainDelays.Add(minutes);

        public void TurnOn(int zoneId) { }
        public void TurnOff(int zoneId) { }
        public void Toggle(int zoneId, bool on) { }
        public void StopAll() { }
        public void Pause(int seconds) { }
        public void Resume() { }
        public void ReloadConfig() { }
    }

    private sealed class FakeStateHub(StatusSnapshot? latest) : IStateHub
    {
        public StatusSnapshot? Latest { get; } = latest;
        public event Action<StatusSnapshot>? SnapshotPublished;
        public void Publish(StatusSnapshot snapshot) => SnapshotPublished?.Invoke(snapshot);
    }

    private sealed class FakeZoneRepository(params Zone[] zones) : IZoneRepository
    {
        private readonly IReadOnlyList<Zone> _zones = zones;
        public Task<IReadOnlyList<Zone>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(_zones);
        public Task<Zone?> GetAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(_zones.FirstOrDefault(z => z.Id == id));
        public Task UpdateAsync(Zone zone, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeProgramRepository(params Program[] programs) : IProgramRepository
    {
        private readonly IReadOnlyList<Program> _programs = programs;
        public Task<IReadOnlyList<Program>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(_programs);
        public Task<Program?> GetWithDetailsAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(_programs.FirstOrDefault(p => p.Id == id));
        public Task<int> AddAsync(Program program, CancellationToken ct = default) => Task.FromResult(0);
        public Task UpdateAsync(Program program, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
