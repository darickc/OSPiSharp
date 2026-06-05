using System.Collections.Immutable;
using OSPi.Domain.Enums;

namespace OSPi.Domain.Scheduling;

/// <summary>
/// A zone awaiting scheduling — the planner's input. Durations are already water-level
/// scaled by the caller; the planner only assigns timing.
/// </summary>
public readonly record struct PendingZone(int ZoneId, int HardwareBit, ZoneGroup Group, int DurationSeconds, int ProgramId);

/// <summary>
/// A scheduled queue entry. All times are <em>integer seconds relative to the planning
/// base</em> (the base represents the firmware's <c>curr_time</c>); the engine maps these
/// to absolute instants. Offsets may be negative for an item that is already running.
/// </summary>
public readonly record struct QueueItem(
    int ZoneId,
    int HardwareBit,
    ZoneGroup Group,
    int StartOffsetSeconds,
    int DurationSeconds,
    int DequeueOffsetSeconds,
    int ProgramId);

/// <summary>
/// A configured master station and the zones bound to it, with lead/lag adjustments.
/// </summary>
public readonly record struct MasterBinding(
    int MasterHardwareBit,
    int OnAdjustSeconds,
    int OffAdjustSeconds,
    ImmutableHashSet<int> BoundZoneIds)
{
    /// <summary>
    /// Build a binding, applying the firmware's 0-coercion: a configured on-adjust of 0
    /// becomes -1 and an off-adjust of 0 becomes +1, so the master staggers 1 second around
    /// the zone rather than tracking it exactly (OpenSprinkler.cpp:1745, 1750).
    /// </summary>
    public static MasterBinding Create(int masterHardwareBit, int onAdjustConfigured, int offAdjustConfigured, IEnumerable<int> boundZoneIds) =>
        new(masterHardwareBit,
            onAdjustConfigured != 0 ? onAdjustConfigured : -1,
            offAdjustConfigured != 0 ? offAdjustConfigured : 1,
            boundZoneIds.ToImmutableHashSet());

    /// <summary>Whether the given zone activates this master.</summary>
    public bool IsBound(int zoneId) => BoundZoneIds.Contains(zoneId);
}

/// <summary>Inputs to a planning pass.</summary>
public sealed record PlanRequest
{
    /// <summary>Zones to schedule, pre-sorted by <c>RunOrder</c> by the caller.</summary>
    public ImmutableArray<PendingZone> NewZones { get; init; } = ImmutableArray<PendingZone>.Empty;

    /// <summary>The current runtime queue (already-scheduled items), relative to the same base.</summary>
    public ImmutableArray<QueueItem> ExistingItems { get; init; } = ImmutableArray<QueueItem>.Empty;

    /// <summary>Inter-zone delay within a sequential group (firmware station delay).</summary>
    public int StationDelaySeconds { get; init; }

    /// <summary>
    /// When true, new sequential zones preempt existing ones (firmware <c>qo &gt; 0</c>);
    /// when false, they are appended after the group's current last stop.
    /// </summary>
    public bool InsertFront { get; init; }

    /// <summary>Configured master stations.</summary>
    public ImmutableArray<MasterBinding> Masters { get; init; } = ImmutableArray<MasterBinding>.Empty;
}

/// <summary>The resulting runtime queue after a planning pass.</summary>
public sealed record SchedulePlan(ImmutableArray<QueueItem> Items);

/// <summary>
/// Pure port of the firmware's queue scheduler. Assigns start/duration/dequeue times to
/// queued zones, honoring sequential groups, station delay, parallel concurrency, and
/// master lead/lag. No I/O, no clock, no globals — a deterministic transform of one queue
/// state into the next.
///
/// <para>Oracle: <c>schedule_all_stations</c> (main.cpp:1475-1618) and
/// <c>handle_master_adjustments</c> (main.cpp:1440-1467). The planning base maps to the
/// firmware's <c>curr_time</c>, so a relative offset of 0 is "now".</para>
/// </summary>
public static class StationScheduler
{
    /// <summary>Number of sequential groups (firmware <c>NUM_SEQ_GROUPS</c>).</summary>
    private const int SeqGroups = 4;

    /// <summary>
    /// Plan the queue. Returns the existing items (shifted/trimmed if
    /// <see cref="PlanRequest.InsertFront"/>) followed by the newly scheduled zones.
    /// </summary>
    public static SchedulePlan Plan(PlanRequest request)
    {
        int delay = request.StationDelaySeconds;
        var existing = request.ExistingItems.ToArray(); // mutable copies for the insert-front shift
        var newZones = request.NewZones;

        // Stagger: mark each sequential group that has a new zone, then prefix-sum so groups
        // start 1s apart (main.cpp:1485-1495). A marked group is offset by the count of
        // marked groups at or before it.
        var stagger = new int[SeqGroups];
        foreach (var z in newZones)
            if (IsSequential(z.Group)) stagger[Gid(z.Group)] = 1;
        for (int i = 1; i < SeqGroups; i++) stagger[i] += stagger[i - 1];

        // last_seq_stop_times derived from the current queue: the latest stop per sequential
        // group (main.cpp tracks this as persistent state; deriving it keeps us pure).
        var lastStop = new long[SeqGroups];
        foreach (var it in existing)
            if (IsSequential(it.Group))
            {
                int g = Gid(it.Group);
                long stop = it.StartOffsetSeconds + it.DurationSeconds;
                if (stop > lastStop[g]) lastStop[g] = stop;
            }

        const long conBase = 0; // curr_time, relative
        long conStart = conBase;
        var seqStart = new long[SeqGroups];

        if (request.InsertFront)
        {
            // First pass: how much time the new sequential zones need per group (main.cpp:1504-1515).
            var seqAdj = new long[SeqGroups];
            foreach (var z in newZones)
                if (IsSequential(z.Group)) seqAdj[Gid(z.Group)] += z.DurationSeconds + delay;

            // Second pass: shift existing sequential zones back to make room (main.cpp:1518-1547).
            for (int k = 0; k < existing.Length; k++)
            {
                var it = existing[k];
                if (!IsSequential(it.Group)) continue; // parallel items are not shifted
                int g = Gid(it.Group);
                long adjustment = seqAdj[g] + stagger[g];
                if (adjustment == 0) continue;

                int st = it.StartOffsetSeconds, dur = it.DurationSeconds, deq = it.DequeueOffsetSeconds;
                if (st <= conBase && conBase < st + dur)
                {
                    // Currently running: trim to the remaining duration and reschedule after the new zones.
                    long remaining = dur - (conBase - st);
                    st = (int)adjustment;
                    dur = (int)remaining;
                    deq = (int)(deq + adjustment);
                }
                else if (conBase < st)
                {
                    // Waiting: push start and dequeue back.
                    st = (int)(st + adjustment);
                    deq = (int)(deq + adjustment);
                }

                existing[k] = it with { StartOffsetSeconds = st, DurationSeconds = dur, DequeueOffsetSeconds = deq };
                long stop = st + dur;
                if (stop > lastStop[g]) lastStop[g] = stop;
            }

            // New sequential zones start at the front (main.cpp:1550-1552).
            for (int i = 0; i < SeqGroups; i++) seqStart[i] = conStart + stagger[i];
        }
        else
        {
            // Append: start after the group's current last stop, else at the front (main.cpp:1556-1562).
            for (int i = 0; i < SeqGroups; i++)
            {
                seqStart[i] = conStart + stagger[i];
                if (lastStop[i] > conBase) seqStart[i] = lastStop[i] + delay;
            }
        }

        conStart += stagger[SeqGroups - 1] + 1; // 1s after the accumulated stagger (main.cpp:1565)

        var scheduled = new List<QueueItem>(newZones.Length);
        foreach (var z in newZones)
        {
            bool sequential = IsSequential(z.Group);
            int g = sequential ? Gid(z.Group) : -1;
            long st;
            if (sequential)
            {
                st = seqStart[g];
                seqStart[g] += z.DurationSeconds;
                seqStart[g] += delay;                 // main.cpp:1576-1578
            }
            else
            {
                st = conStart;
                conStart += 1;                        // main.cpp:1581-1583
            }

            // Master adjustments (handle_master_adjustments, main.cpp:1440-1467).
            int startAdj = 0, dequeueAdj = 0;
            foreach (var m in request.Masters)
                if (m.IsBound(z.ZoneId))
                {
                    startAdj = Math.Min(startAdj, m.OnAdjustSeconds);
                    dequeueAdj = Math.Max(dequeueAdj, m.OffAdjustSeconds);
                }

            int absStart = Math.Abs(startAdj);
            if (st - conBase <= absStart)
            {
                // Negative master-on lead would fall before now: push the zone back so the
                // master has time to engage (main.cpp:1461-1463). Only a sequential group's
                // cursor advances; the firmware's write to seq_start_times[gid] for a parallel
                // zone (gid 255) is an out-of-bounds bug we intentionally do not reproduce.
                st += absStart;
                if (sequential) seqStart[g] += absStart;
            }

            long deque = st + z.DurationSeconds + dequeueAdj; // main.cpp:1466

            scheduled.Add(new QueueItem(z.ZoneId, z.HardwareBit, z.Group, (int)st, z.DurationSeconds, (int)deque, z.ProgramId));
        }

        var items = ImmutableArray.CreateBuilder<QueueItem>(existing.Length + scheduled.Count);
        items.AddRange(existing);
        items.AddRange(scheduled);
        return new SchedulePlan(items.ToImmutable());
    }

    /// <summary>
    /// Whether a master station's output should be on at the given moment: true iff some
    /// bound zone is within <c>[start + onAdj, start + dur + offAdj]</c> (main.cpp:1081-1082).
    /// </summary>
    public static bool MasterShouldBeOn(MasterBinding master, ImmutableArray<QueueItem> items, long currentRelativeSeconds)
    {
        foreach (var it in items)
        {
            if (!master.IsBound(it.ZoneId)) continue;
            long on = it.StartOffsetSeconds + master.OnAdjustSeconds;
            long off = it.StartOffsetSeconds + it.DurationSeconds + master.OffAdjustSeconds;
            if (currentRelativeSeconds >= on && currentRelativeSeconds <= off) return true;
        }
        return false;
    }

    /// <summary>
    /// Sequential = one of the four sequential groups. Parallel and Independent run
    /// concurrently (firmware <c>is_sequential_station</c>, OpenSprinkler.cpp:1722).
    /// Independent has no firmware counterpart; we treat it as concurrent.
    /// </summary>
    public static bool IsSequential(ZoneGroup g) =>
        g is ZoneGroup.Sequential0 or ZoneGroup.Sequential1 or ZoneGroup.Sequential2 or ZoneGroup.Sequential3;

    private static int Gid(ZoneGroup g) => (int)g; // 0..3 for the sequential groups
}
