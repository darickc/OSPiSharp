using System.Collections.Immutable;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OSPi.Application.Hardware;
using OSPi.Application.Persistence;
using OSPi.Application.Services;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;
using OSPi.Domain.Scheduling;

namespace OSPi.Application.Engine;

/// <summary>
/// The single owner of mutable runtime state and the only writer to hardware. Each second it
/// drains queued <see cref="EngineCommand"/>s, matches programs once per minute, executes the
/// in-memory runtime queue, applies the resulting zone state to the <see cref="IZoneDriver"/>,
/// and publishes a <see cref="StatusSnapshot"/>.
///
/// <para>This mirrors the firmware's <c>do_loop</c>: a per-minute program-matching gate that
/// enqueues stations and calls the planner once, plus a per-second queue execution that turns
/// stations on/off, drives master stations, and dequeues expired items. The pure scheduling
/// functions (<see cref="ProgramMatcher"/>, <see cref="StationScheduler"/>) carry the timing
/// logic; this class only owns the live, absolute-time queue and the I/O.</para>
///
/// <para>Sensors (rain/soil binary sensors, program switch, flow) are deliberately not ported —
/// this OSPi clone has no sensor inputs. <c>Zone.IgnoreSensor</c> remains dormant.</para>
/// </summary>
public sealed class SprinklerEngine : BackgroundService
{
    private readonly IZoneDriver _driver;
    private readonly IStateHub _stateHub;
    private readonly ILogger<SprinklerEngine> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISolarCalculator _solar;
    private readonly IRunLogWriter _runLog;
    private readonly TimeProvider _clock;

    private readonly Channel<EngineCommand> _commands =
        Channel.CreateUnbounded<EngineCommand>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Final desired on/off state per hardware bit. Mutated only on the loop thread.</summary>
    private readonly bool[] _desired;

    /// <summary>
    /// Queue-driven on state per bit, before the pause-suppress and manual overlays. Run logging
    /// keys off this (not <see cref="_desired"/>) so a pause does not spuriously close+reopen a run
    /// and indefinite manual toggles — which never enter the queue — are excluded from history.
    /// </summary>
    private readonly bool[] _logicalOn;

    /// <summary>Indefinite manual overrides (Phase-0 dashboard toggles), OR'd over the queue.</summary>
    private readonly bool[] _manual;

    /// <summary>A scheduled queue entry in absolute UTC epoch seconds (the live counterpart of <see cref="QueueItem"/>).</summary>
    private sealed record LiveQueueItem(
        int ZoneId, int HardwareBit, ZoneGroup Group,
        long StartEpochSec, int DurationSeconds, long DequeueEpochSec, int ProgramId);

    /// <summary>The runtime queue (ephemeral, like the firmware's <c>queue[]</c>). Loop-thread only.</summary>
    private readonly List<LiveQueueItem> _queue = new();

    /// <summary>Per-bit earliest-start assignment for the current tick, retained for status projection.</summary>
    private readonly LiveQueueItem?[] _assignedByBit;

    /// <summary>An open (still-running) run being timed for the run log, per hardware bit.</summary>
    private readonly record struct OpenRun(int ZoneId, long StartEpochSec, int? ProgramId);

    /// <summary>Per-bit run currently being timed, or null when the bit is logically off. Loop-thread only.</summary>
    private readonly OpenRun?[] _openRuns;

    private long _lastEpochMinute = long.MinValue;
    private long _pauseUntilSec;
    private DateTimeOffset? _rainDelayUntil;

    // Cached config snapshot + derived lookups. Written only on the loop thread; an off-thread
    // reload stages a replacement into _pendingData, adopted at the top of the next tick.
    private SchedulingData _data = EmptyData();
    private SchedulingData? _pendingData;
    private Dictionary<int, Zone> _zonesById = new();
    private Dictionary<int, Zone> _zonesByBit = new();
    private ImmutableArray<MasterBinding> _masters = ImmutableArray<MasterBinding>.Empty;
    private HashSet<int> _masterOutputZoneIds = new();
    private readonly HashSet<int> _deletedThisSession = new();

    private CancellationToken _stoppingToken;

    public SprinklerEngine(
        IZoneDriver driver,
        IStateHub stateHub,
        ILogger<SprinklerEngine> logger,
        IServiceScopeFactory scopeFactory,
        ISolarCalculator solar,
        IRunLogWriter? runLog = null,
        TimeProvider? clock = null)
    {
        _driver = driver;
        _stateHub = stateHub;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _solar = solar;
        _runLog = runLog ?? new NullRunLogWriter();
        _clock = clock ?? TimeProvider.System;
        _desired = new bool[driver.ZoneCount];
        _logicalOn = new bool[driver.ZoneCount];
        _manual = new bool[driver.ZoneCount];
        _assignedByBit = new LiveQueueItem?[driver.ZoneCount];
        _openRuns = new OpenRun?[driver.ZoneCount];
    }

    /// <summary>Post a command to be applied at the top of the next tick.</summary>
    public void Post(EngineCommand command) => _commands.Writer.TryWrite(command);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        _logger.LogInformation("SprinklerEngine started with {ZoneCount} zones.", _driver.ZoneCount);

        try
        {
            ReplaceConfig(await LoadConfigAsync(stoppingToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial scheduling-data load failed; starting with empty config.");
            ReplaceConfig(EmptyData());
        }
        _rainDelayUntil = _data.Settings.RainDelayUntil;

        // Ensure hardware starts in a known (all-off) state.
        _driver.Apply(_desired);
        Publish(_clock.GetUtcNow().ToUnixTimeSeconds(), paused: false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            // Close any run still open so a coil energized at shutdown produces a history row.
            // Best-effort: a hard process exit may drop the in-flight off-thread write.
            FlushOpenRuns(_clock.GetUtcNow().ToUnixTimeSeconds());
            Array.Clear(_desired);
            _driver.Apply(_desired);
            _logger.LogInformation("SprinklerEngine stopped; all zones turned off.");
        }
    }

    /// <summary>
    /// Seed cached config and apply the initial all-off state without running the hosted loop.
    /// Test-only: lets a test drive <see cref="Tick"/> synchronously against a
    /// <c>FakeTimeProvider</c> for deterministic, fast per-second simulation.
    /// </summary>
    internal void PrimeForTest(SchedulingData data)
    {
        ReplaceConfig(data);
        _rainDelayUntil = data.Settings.RainDelayUntil;
        _driver.Apply(_desired);
        Publish(_clock.GetUtcNow().ToUnixTimeSeconds(), paused: false);
    }

    internal void Tick()
    {
        // Adopt any config staged by an off-thread reload (never mid-tick).
        var fresh = Interlocked.Exchange(ref _pendingData, null);
        if (fresh is not null) ReplaceConfig(fresh);

        long nowSec = _clock.GetUtcNow().ToUnixTimeSeconds();
        DrainCommands(nowSec);

        // Minute-roll gate on UTC epoch seconds. Under a fixed offset (no DST) this rolls at the
        // same instant as civil minutes; civil time is built only inside matching. Revisit this
        // invariant if DST is ever introduced.
        long nowMin = nowSec / 60;
        if (nowMin != _lastEpochMinute)
        {
            _lastEpochMinute = nowMin;
            RunPerMinuteMatching(nowSec);
        }

        RunPerSecondQueueExecution(nowSec);
        TrackRuns(nowSec);
        DriveAndPublish(nowSec);
    }

    private void DrainCommands(long nowSec)
    {
        while (_commands.Reader.TryRead(out var command))
        {
            switch (command)
            {
                case EngineCommand.SetZone set when IsValidZone(set.ZoneId):
                    _manual[set.ZoneId] = set.On;
                    break;
                case EngineCommand.SetZone set:
                    _logger.LogWarning("Ignoring SetZone for out-of-range zone {ZoneId}.", set.ZoneId);
                    break;
                case EngineCommand.StopAll:
                    Array.Clear(_manual);
                    _queue.Clear();
                    break;
                case EngineCommand.RunProgram run:
                    HandleRunProgram(run.ProgramId, nowSec);
                    break;
                case EngineCommand.RunZoneTimed run:
                    HandleRunZoneTimed(run.ZoneId, run.Seconds, nowSec);
                    break;
                case EngineCommand.CancelZone cancel when IsValidZone(cancel.HardwareBit):
                    // Remove the zone's queued/running item and any manual override. The next
                    // per-second pass sees _logicalOn[bit] go false and closes the run-log entry.
                    _queue.RemoveAll(q => q.HardwareBit == cancel.HardwareBit);
                    _manual[cancel.HardwareBit] = false;
                    break;
                case EngineCommand.CancelZone cancel:
                    _logger.LogWarning("Ignoring CancelZone for out-of-range bit {Bit}.", cancel.HardwareBit);
                    break;
                case EngineCommand.SetRainDelay rd:
                    HandleSetRainDelay(rd.Minutes);
                    break;
                case EngineCommand.Pause pause:
                    _pauseUntilSec = pause.Seconds > 0 ? nowSec + pause.Seconds : 0;
                    break;
                case EngineCommand.Resume:
                    _pauseUntilSec = 0;
                    break;
                case EngineCommand.ReloadConfig:
                    KickReload();
                    break;
            }
        }
    }

    // ===== Per-minute program matching (firmware: match all, then schedule_all_stations once) =====

    private void RunPerMinuteMatching(long nowSec)
    {
        if (_data.Programs.Count == 0) return;

        var civil = new CivilInstant((_clock.GetUtcNow() + TimeSpan.FromMinutes(_data.Settings.UtcOffsetMinutes)).DateTime);
        var (sunrise, sunset) = _solar.ForDate(_data.Settings, civil.Date);
        bool rainActive = IsRainActive();

        var pending = new List<PendingZone>();
        var toDelete = new List<int>();

        foreach (var p in _data.Programs)
        {
            if (_deletedThisSession.Contains(p.Id)) continue;

            var match = ProgramMatcher.CheckMatch(p, civil, sunrise, sunset);
            if (!match.Matched) continue;

            BuildPendingZones(p, rainActive, pending);
            if (match.ShouldDelete) toDelete.Add(p.Id);
        }

        if (pending.Count > 0) RunPlan(pending, nowSec, insertFront: false);

        foreach (var id in toDelete) SignalDelete(id);
    }

    /// <summary>
    /// Expand a program's per-zone durations into <see cref="PendingZone"/>s: ordered by
    /// RunOrder, water-level scaled (with the &lt;20%/&lt;10s skip), skipping disabled zones,
    /// master outputs, and — when <paramref name="rainActive"/> — zones that do not ignore rain.
    /// </summary>
    private void BuildPendingZones(Domain.Entities.Program p, bool rainActive, List<PendingZone> sink)
    {
        int wl = p.UseWeather ? _data.Settings.WaterLevelPercent : 100;

        foreach (var d in p.ZoneDurations.OrderBy(static z => z.RunOrder))
        {
            if (!_zonesById.TryGetValue(d.ZoneId, out var zone)) continue;
            if (zone.Disabled) continue;
            if (_masterOutputZoneIds.Contains(zone.Id)) continue;
            if (rainActive && !zone.IgnoreRain) continue;

            long water = (long)d.DurationSeconds * wl / 100;   // firmware integer order
            if (wl < 20 && water < 10) water = 0;              // <20%/<10s skip
            if (water <= 0) continue;

            sink.Add(new PendingZone(zone.Id, zone.HardwareBit, zone.Group, (int)water, p.Id));
        }
    }

    // ===== Per-second queue execution (firmware: assign earliest start, time-keep, dequeue) =====

    private void RunPerSecondQueueExecution(long nowSec)
    {
        Array.Clear(_desired);
        Array.Clear(_logicalOn);
        Array.Clear(_assignedByBit);

        // Assign the earliest-start queue item to each hardware bit (firmware station_qid).
        foreach (var q in _queue)
        {
            int bit = q.HardwareBit;
            if (bit < 0 || bit >= _assignedByBit.Length) continue;
            var current = _assignedByBit[bit];
            if (current is null || q.StartEpochSec < current.StartEpochSec) _assignedByBit[bit] = q;
        }

        // Time-keeping: a zone is on iff now is within [start, start + duration). This is the
        // queue-driven "logical on" state; run logging keys off it (before pause/manual overlays).
        for (int bit = 0; bit < _assignedByBit.Length; bit++)
        {
            var q = _assignedByBit[bit];
            if (q is null) continue;
            if (nowSec >= q.StartEpochSec && nowSec < q.StartEpochSec + q.DurationSeconds) _logicalOn[bit] = true;
        }

        // Master stations: on iff some bound zone is within its lead/lag window (relative to now).
        if (_masters.Length > 0 && _queue.Count > 0)
        {
            var rel = LiveToRelative(nowSec);
            foreach (var m in _masters)
            {
                if (m.MasterHardwareBit < 0 || m.MasterHardwareBit >= _logicalOn.Length) continue;
                if (StationScheduler.MasterShouldBeOn(m, rel, 0)) _logicalOn[m.MasterHardwareBit] = true;
            }
        }

        // _desired starts as a copy of the queue-driven state; pause/manual overlays are applied
        // later in DriveAndPublish without disturbing the run-logging signal.
        Array.Copy(_logicalOn, _desired, _desired.Length);

        // Dequeue expired items (firmware: !dur || now >= deque_time).
        _queue.RemoveAll(q => q.DurationSeconds <= 0 || nowSec >= q.DequeueEpochSec);
    }

    // ===== Run-log tracking (firmware: log a station when it turns off) =====

    /// <summary>
    /// Detect run start/end transitions from the queue-driven <see cref="_logicalOn"/> state and
    /// emit a run-log entry for each contiguous run as it ends. Keying off the logical state (not
    /// the pause/manual-masked <see cref="_desired"/>) means a pause logs one spanning run, and
    /// indefinite manual zones — never in the queue — are not logged.
    /// </summary>
    private void TrackRuns(long nowSec)
    {
        for (int bit = 0; bit < _logicalOn.Length; bit++)
        {
            var open = _openRuns[bit];
            bool wasOpen = open is not null;
            bool isOnNow = _logicalOn[bit];
            var assigned = _assignedByBit[bit];

            if (isOnNow && !wasOpen)
            {
                // Run just started. Masters (no assigned queue item) are not logged.
                if (assigned is not null) _openRuns[bit] = OpenFrom(assigned, nowSec);
            }
            else if (wasOpen && !isOnNow)
            {
                // Run ended: expiry, preempt-trim, StopAll, or rain-delay clear.
                CloseRun(bit, nowSec);
            }
            else if (wasOpen && isOnNow && assigned is not null && assigned.ZoneId != open!.Value.ZoneId)
            {
                // A different queue item took over the same bit with no intervening off-second.
                CloseRun(bit, nowSec);
                _openRuns[bit] = OpenFrom(assigned, nowSec);
            }
        }
    }

    private static OpenRun OpenFrom(LiveQueueItem item, long startSec) =>
        new(item.ZoneId, startSec, item.ProgramId == 0 ? null : item.ProgramId);

    /// <summary>Close the open run on <paramref name="bit"/> and persist it (observed duration).</summary>
    private void CloseRun(int bit, long endEpochSec)
    {
        var open = _openRuns[bit];
        if (open is null) return;
        _openRuns[bit] = null;

        var run = open.Value;
        int duration = (int)(endEpochSec - run.StartEpochSec);
        if (duration <= 0) return; // a run observed for <1s carries no useful history

        _runLog.Write(
            run.ZoneId,
            run.ProgramId,
            DateTimeOffset.FromUnixTimeSeconds(run.StartEpochSec),
            DateTimeOffset.FromUnixTimeSeconds(endEpochSec),
            duration);
    }

    /// <summary>Close every open run as of <paramref name="nowSec"/> (StopAll, shutdown).</summary>
    private void FlushOpenRuns(long nowSec)
    {
        for (int bit = 0; bit < _openRuns.Length; bit++) CloseRun(bit, nowSec);
    }

    private void DriveAndPublish(long nowSec)
    {
        bool paused = nowSec < _pauseUntilSec;

        if (paused)
        {
            // Suppress all output while paused; the queue keeps advancing (firmware pause).
            Array.Clear(_desired);
        }
        else
        {
            // Overlay indefinite manual zones (these do not drive master stations).
            for (int i = 0; i < _desired.Length; i++)
                if (_manual[i]) _desired[i] = true;
        }

        _driver.Apply(_desired);
        Publish(nowSec, paused);
    }

    private void Publish(long nowSec, bool paused)
    {
        var now = _clock.GetUtcNow();
        var zones = ImmutableArray.CreateBuilder<ZoneStatus>(_desired.Length);
        for (int bit = 0; bit < _desired.Length; bit++)
        {
            var q = _assignedByBit[bit];
            int? remaining = null;
            int? programId = null;
            bool queued = false;

            if (q is not null)
            {
                programId = q.ProgramId == 0 ? null : q.ProgramId;
                if (_desired[bit] && nowSec < q.StartEpochSec + q.DurationSeconds)
                    remaining = (int)(q.StartEpochSec + q.DurationSeconds - nowSec);
                else if (nowSec < q.StartEpochSec)
                    queued = true;
            }

            zones.Add(new ZoneStatus
            {
                ZoneId = bit,
                On = _desired[bit],
                SecondsRemaining = remaining,
                ProgramId = programId,
                Queued = queued,
            });
        }

        _stateHub.Publish(new StatusSnapshot
        {
            TimestampUtc = now,
            Zones = zones.MoveToImmutable(),
            Paused = paused,
            RainDelayUntil = _rainDelayUntil is { } until && until > now ? _rainDelayUntil : null,
            WaterLevelPercent = _data.Settings.WaterLevelPercent,
        });
    }

    // ===== Command handlers (loop thread) =====

    private void HandleRunProgram(int programId, long nowSec)
    {
        var program = _data.Programs.FirstOrDefault(p => p.Id == programId);
        if (program is null)
        {
            _logger.LogWarning("RunProgram: program {ProgramId} not found.", programId);
            return;
        }

        var pending = new List<PendingZone>();
        BuildPendingZones(program, rainActive: false, pending); // manual runs ignore rain delay
        if (pending.Count > 0) RunPlan(pending, nowSec, insertFront: true);
    }

    private void HandleRunZoneTimed(int hardwareBit, int seconds, long nowSec)
    {
        if (seconds <= 0) return;
        if (!_zonesByBit.TryGetValue(hardwareBit, out var zone))
        {
            _logger.LogWarning("RunZoneTimed: no zone with hardware bit {Bit}.", hardwareBit);
            return;
        }
        if (zone.Disabled) return;

        var pending = new List<PendingZone> { new(zone.Id, zone.HardwareBit, zone.Group, seconds, 0) };
        RunPlan(pending, nowSec, insertFront: true);
    }

    private void HandleSetRainDelay(int minutes)
    {
        if (minutes > 0)
        {
            _rainDelayUntil = _clock.GetUtcNow().AddMinutes(minutes);
            // Stop queued/running zones that do not ignore rain.
            _queue.RemoveAll(q => !(_zonesById.TryGetValue(q.ZoneId, out var z) && z.IgnoreRain));
        }
        else
        {
            _rainDelayUntil = null;
        }
        PersistRainDelay(_rainDelayUntil);
    }

    // ===== Planning + offset/absolute conversion (planning base = nowSec) =====

    private void RunPlan(List<PendingZone> pending, long nowSec, bool insertFront)
    {
        var plan = StationScheduler.Plan(new PlanRequest
        {
            NewZones = pending.ToImmutableArray(),
            ExistingItems = LiveToRelative(nowSec),
            StationDelaySeconds = _data.Settings.StationDelaySeconds,
            InsertFront = insertFront,
            Masters = _masters,
        });
        MergePlan(plan, nowSec);
    }

    private ImmutableArray<QueueItem> LiveToRelative(long nowSec)
    {
        if (_queue.Count == 0) return ImmutableArray<QueueItem>.Empty;

        var builder = ImmutableArray.CreateBuilder<QueueItem>(_queue.Count);
        foreach (var q in _queue)
        {
            // Offsets may be negative for an item already running; this is required by the
            // planner's insert-front "currently running" trim branch — do not normalize away.
            builder.Add(new QueueItem(
                q.ZoneId, q.HardwareBit, q.Group,
                (int)(q.StartEpochSec - nowSec),
                q.DurationSeconds,
                (int)(q.DequeueEpochSec - nowSec),
                q.ProgramId));
        }
        return builder.MoveToImmutable();
    }

    private void MergePlan(SchedulePlan plan, long nowSec)
    {
        _queue.Clear();
        foreach (var it in plan.Items)
        {
            _queue.Add(new LiveQueueItem(
                it.ZoneId, it.HardwareBit, it.Group,
                nowSec + it.StartOffsetSeconds,
                it.DurationSeconds,
                nowSec + it.DequeueOffsetSeconds,
                it.ProgramId));
        }
    }

    // ===== Config caching + off-thread persistence =====

    private async Task<SchedulingData> LoadConfigAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchedulingDataRepository>();
        return await repo.LoadAllAsync(ct);
    }

    private void KickReload()
    {
        var ct = _stoppingToken;
        _ = Task.Run(async () =>
        {
            try { Interlocked.Exchange(ref _pendingData, await LoadConfigAsync(ct)); }
            catch (Exception ex) { _logger.LogError(ex, "Config reload failed."); }
        }, ct);
    }

    private void SignalDelete(int programId)
    {
        _deletedThisSession.Add(programId); // guard against re-match before the reload lands
        var ct = _stoppingToken;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IProgramRepository>();
                await repo.DeleteAsync(programId, ct);
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to delete single-run program {ProgramId}.", programId); }
            Post(new EngineCommand.ReloadConfig());
        }, ct);
    }

    private void PersistRainDelay(DateTimeOffset? until)
    {
        var ct = _stoppingToken;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IControllerSettingsRepository>();
                var settings = await repo.GetAsync(ct);
                settings.RainDelayUntil = until;
                await repo.UpdateAsync(settings, ct);
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to persist rain delay."); }
        }, ct);
    }

    private void ReplaceConfig(SchedulingData data)
    {
        _data = data;
        _zonesById = data.Zones.ToDictionary(z => z.Id);
        _zonesByBit = data.Zones.ToDictionary(z => z.HardwareBit);

        var masterOutputs = new HashSet<int>();
        var masters = ImmutableArray.CreateBuilder<MasterBinding>();
        foreach (var m in data.Masters)
        {
            if (m.ZoneId is not { } zoneId) continue;
            if (!_zonesById.TryGetValue(zoneId, out var outputZone)) continue;
            masterOutputs.Add(zoneId);

            var bound = data.Zones
                .Where(z => m.MasterIndex == 1 ? z.BoundToMaster1 : m.MasterIndex == 2 && z.BoundToMaster2)
                .Select(z => z.Id);
            masters.Add(MasterBinding.Create(outputZone.HardwareBit, m.OnAdjustSeconds, m.OffAdjustSeconds, bound));
        }
        _masterOutputZoneIds = masterOutputs;
        _masters = masters.ToImmutable();
    }

    private bool IsRainActive() => _rainDelayUntil is { } until && until > _clock.GetUtcNow();

    private bool IsValidZone(int zoneId) => zoneId >= 0 && zoneId < _desired.Length;

    private static SchedulingData EmptyData() => new(
        Array.Empty<Domain.Entities.Program>(),
        Array.Empty<Zone>(),
        Array.Empty<MasterStation>(),
        new ControllerSettings());
}
