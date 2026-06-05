using System.Collections.Immutable;

namespace OSPi.Application.Engine;

/// <summary>Immutable per-second projection of engine state, pushed to the UI.</summary>
public sealed record StatusSnapshot
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required ImmutableArray<ZoneStatus> Zones { get; init; }
    public bool SystemEnabled { get; init; } = true;

    /// <summary>Whether hardware output is currently paused.</summary>
    public bool Paused { get; init; }

    /// <summary>Seconds until the current pause expires, or 0 when not paused.</summary>
    public int PauseSecondsRemaining { get; init; }

    /// <summary>If a rain delay is active, the instant it expires; otherwise null.</summary>
    public DateTimeOffset? RainDelayUntil { get; init; }

    /// <summary>The global water-level scaling percentage in effect.</summary>
    public int WaterLevelPercent { get; init; } = 100;
}

public sealed record ZoneStatus
{
    public required int ZoneId { get; init; }
    public required bool On { get; init; }
    /// <summary>Seconds remaining for a timed run, or null for indefinite/manual/off.</summary>
    public int? SecondsRemaining { get; init; }

    /// <summary>The program whose run owns this zone, or null for manual/off.</summary>
    public int? ProgramId { get; init; }

    /// <summary>True when the zone is enqueued but its start time is still in the future.</summary>
    public bool Queued { get; init; }

    /// <summary>True when the zone was running but is frozen mid-run by a system pause.</summary>
    public bool Paused { get; init; }
}
