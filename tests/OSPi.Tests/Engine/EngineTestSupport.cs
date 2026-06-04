using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OSPi.Application.Engine;
using OSPi.Application.Hardware;
using OSPi.Application.Persistence;
using OSPi.Application.Services;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;

namespace OSPi.Tests.Engine;

/// <summary>Captures the last bit array applied, standing in for hardware.</summary>
internal sealed class CapturingDriver : IZoneDriver
{
    public CapturingDriver(int zoneCount) => ZoneCount = zoneCount;
    public int ZoneCount { get; }
    public bool[] Last { get; private set; } = Array.Empty<bool>();
    public void Apply(ReadOnlySpan<bool> zoneStates) => Last = zoneStates.ToArray();
}

/// <summary>Returns a fixed (mutable) <see cref="SchedulingData"/> snapshot.</summary>
internal sealed class FakeSchedulingDataRepository : ISchedulingDataRepository
{
    public SchedulingData Data { get; set; }
    public FakeSchedulingDataRepository(SchedulingData data) => Data = data;
    public Task<SchedulingData> LoadAllAsync(CancellationToken ct = default) => Task.FromResult(Data);
}

/// <summary>Records program deletions; other operations are unused by the engine.</summary>
internal sealed class RecordingProgramRepository : IProgramRepository
{
    public List<int> DeletedIds { get; } = new();

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        lock (DeletedIds) DeletedIds.Add(id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Domain.Entities.Program>> GetAllAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<Domain.Entities.Program?> GetWithDetailsAsync(int id, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<int> AddAsync(Domain.Entities.Program program, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task UpdateAsync(Domain.Entities.Program program, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

/// <summary>In-memory settings repo for rain-delay persistence.</summary>
internal sealed class FakeControllerSettingsRepository : IControllerSettingsRepository
{
    public ControllerSettings Settings { get; set; } = new();
    public Task<ControllerSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(Settings);
    public Task UpdateAsync(ControllerSettings settings, CancellationToken ct = default)
    {
        Settings = settings;
        return Task.CompletedTask;
    }
}

/// <summary>Records run-log writes synchronously on the tick thread for deterministic assertions.</summary>
internal sealed class RecordingRunLogWriter : IRunLogWriter
{
    public List<RunLogEntry> Entries { get; } = new();

    public void Write(int zoneId, int? programId, DateTimeOffset start, DateTimeOffset end, int durationSeconds) =>
        Entries.Add(new RunLogEntry
        {
            ZoneId = zoneId,
            ProgramId = programId,
            StartTime = start,
            EndTime = end,
            DurationSeconds = durationSeconds,
        });
}

/// <summary>Returns fixed sunrise/sunset minutes regardless of date.</summary>
internal sealed class FixedSolarCalculator : ISolarCalculator
{
    private readonly int _sunrise;
    private readonly int _sunset;
    public FixedSolarCalculator(int sunrise = 360, int sunset = 1080) { _sunrise = sunrise; _sunset = sunset; }
    public (int SunriseMinute, int SunsetMinute) ForDate(ControllerSettings settings, DateOnly date) => (_sunrise, _sunset);
}

/// <summary>Minimal <see cref="IServiceScopeFactory"/> resolving a fixed service map.</summary>
internal sealed class FakeScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    private readonly Dictionary<Type, object> _services;
    public FakeScopeFactory(Dictionary<Type, object> services) => _services = services;
    public IServiceScope CreateScope() => this;
    public IServiceProvider ServiceProvider => this;
    public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);
    public void Dispose() { }
}

/// <summary>
/// Drives a <see cref="SprinklerEngine"/> tick-by-tick against a <see cref="FakeTimeProvider"/>,
/// recording the applied bit array after every tick. Synchronous and deterministic — no hosted
/// loop, no polling. Tick <c>k</c> (1-based) runs at <c>start + k</c> seconds.
/// </summary>
internal sealed class EngineHarness
{
    public SprinklerEngine Engine { get; }
    public CapturingDriver Driver { get; }
    public InMemoryStateHub Hub { get; }
    public FakeTimeProvider Time { get; }
    public RecordingProgramRepository Programs { get; }
    public FakeControllerSettingsRepository SettingsRepo { get; }
    public RecordingRunLogWriter RunLog { get; }

    private readonly List<bool[]> _frames = new(); // _frames[k-1] = state after tick k
    private readonly List<StatusSnapshot> _snaps = new();

    public EngineHarness(SchedulingData data, DateTimeOffset start, int zones = 16,
        int sunrise = 360, int sunset = 1080)
    {
        Driver = new CapturingDriver(zones);
        Hub = new InMemoryStateHub();
        Time = new FakeTimeProvider(start);
        Programs = new RecordingProgramRepository();
        SettingsRepo = new FakeControllerSettingsRepository { Settings = data.Settings };
        RunLog = new RecordingRunLogWriter();

        var scopeFactory = new FakeScopeFactory(new Dictionary<Type, object>
        {
            [typeof(ISchedulingDataRepository)] = new FakeSchedulingDataRepository(data),
            [typeof(IProgramRepository)] = Programs,
            [typeof(IControllerSettingsRepository)] = SettingsRepo,
        });

        Engine = new SprinklerEngine(Driver, Hub, NullLogger<SprinklerEngine>.Instance,
            scopeFactory, new FixedSolarCalculator(sunrise, sunset), RunLog, Time);
        Engine.PrimeForTest(data);
    }

    /// <summary>Advance one second and run a tick. Returns the applied state after the tick.</summary>
    public bool[] Step()
    {
        Time.Advance(TimeSpan.FromSeconds(1));
        Engine.Tick();
        _frames.Add((bool[])Driver.Last.Clone());
        _snaps.Add(Hub.Latest!);
        return Driver.Last;
    }

    /// <summary>Run <paramref name="count"/> ticks.</summary>
    public void Steps(int count)
    {
        for (int i = 0; i < count; i++) Step();
    }

    /// <summary>The applied state recorded after tick <paramref name="k"/> (1-based).</summary>
    public bool[] FrameAt(int k) => _frames[k - 1];

    /// <summary>The snapshot published after tick <paramref name="k"/> (1-based).</summary>
    public StatusSnapshot SnapshotAt(int k) => _snaps[k - 1];

    public StatusSnapshot Snapshot => Hub.Latest!;
}

/// <summary>Builders for hand-authored scheduling data.</summary>
internal static class Build
{
    /// <summary>16 zones, ids 1..16, hardware bits 0..15, all Sequential0.</summary>
    public static List<Zone> Zones16()
    {
        var zones = new List<Zone>();
        for (int i = 0; i < 16; i++)
            zones.Add(new Zone { Id = i + 1, HardwareBit = i, Name = $"Zone {i + 1}", Group = ZoneGroup.Sequential0 });
        return zones;
    }

    public static ControllerSettings Settings(int waterLevel = 100, int stationDelay = 0, int utcOffset = 0) => new()
    {
        Id = 1,
        WaterLevelPercent = waterLevel,
        StationDelaySeconds = stationDelay,
        UtcOffsetMinutes = utcOffset,
    };

    /// <summary>A fixed-start program that matches every weekday at the given minute-of-day.</summary>
    public static Domain.Entities.Program FixedDaily(int id, int startMinute, params (int zoneId, int seconds, int order)[] durations)
    {
        var p = new Domain.Entities.Program
        {
            Id = id,
            Name = $"Program {id}",
            Enabled = true,
            UseWeather = false,
            ScheduleType = ScheduleType.Weekly,
            WeekdayMask = 0x7F, // every day
            StartTimeType = StartTimeType.Fixed,
            StartTimes = { new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = startMinute } },
        };
        foreach (var (zoneId, seconds, order) in durations)
            p.ZoneDurations.Add(new ProgramZoneDuration { ProgramId = id, ZoneId = zoneId, DurationSeconds = seconds, RunOrder = order });
        return p;
    }

    public static SchedulingData Data(ControllerSettings settings, IEnumerable<Zone> zones,
        IEnumerable<Domain.Entities.Program>? programs = null, IEnumerable<MasterStation>? masters = null) =>
        new(
            (programs ?? Array.Empty<Domain.Entities.Program>()).ToList(),
            zones.ToList(),
            (masters ?? Array.Empty<MasterStation>()).ToList(),
            settings);
}
